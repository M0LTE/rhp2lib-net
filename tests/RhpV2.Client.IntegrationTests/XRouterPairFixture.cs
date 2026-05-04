using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Xunit;

namespace RhpV2.Client.IntegrationTests;

/// <summary>
/// Two xrouter containers connected by AXUDP, running on a private
/// Docker network with static IPs that match each container's
/// <c>IPADDRESS=</c> directive.  Driving Node A's RHP and connecting
/// AX.25 to Node B's callsign exercises the full stack: RHP framing →
/// xrouter AX.25 layer 2 → AXUDP encapsulation → IP/UDP across the
/// container network → Node B's AX.25 stack → Node B's command
/// processor.
///
/// <para>
/// Each test class collects the fixture; tests skip gracefully when
/// Docker isn't reachable.
/// </para>
/// </summary>
public sealed class XRouterPairFixture : IAsyncLifetime
{
    private const string Image = "ghcr.io/packethacking/xrouter";
    private const string Subnet = "10.99.99.0/24";

    public string NodeAContainerIp { get; } = "10.99.99.2";
    public string NodeBContainerIp { get; } = "10.99.99.3";

    /// <summary>NODECALL of node A.</summary>
    public string NodeACallsign => "G9DUM-1";
    /// <summary>NODECALL of node B.</summary>
    public string NodeBCallsign => "G8PZT-1";
    /// <summary>CONSOLECALL of node A — used as <c>local</c> when binding.</summary>
    public string NodeAConsoleCallsign => "G9DUM";

    /// <summary>Mapped host port for Node A's RHP listener.</summary>
    public ushort NodeARhpPort { get; private set; }
    /// <summary>Mapped host port for Node B's RHP listener.</summary>
    public ushort NodeBRhpPort { get; private set; }

    public string Host => "127.0.0.1";

    /// <summary>True once both containers are up and RHP is accepting.</summary>
    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    private INetwork? _network;
    private IContainer? _nodeA;
    private IContainer? _nodeB;

    public async Task InitializeAsync()
    {
        try
        {
            _network = new NetworkBuilder()
                .WithName($"axudp-{Guid.NewGuid():N}")
                .WithCreateParameterModifier(p =>
                {
                    p.IPAM = new IPAM
                    {
                        Driver = "default",
                        Config = new List<IPAMConfig> { new() { Subnet = Subnet } },
                    };
                })
                .Build();
            await _network.CreateAsync().ConfigureAwait(false);

            _nodeA = BuildNode("NodeA.CFG", NodeAContainerIp);
            _nodeB = BuildNode("NodeB.CFG", NodeBContainerIp);

            await Task.WhenAll(_nodeA.StartAsync(), _nodeB.StartAsync()).ConfigureAwait(false);

            NodeARhpPort = _nodeA.GetMappedPublicPort(9000);
            NodeBRhpPort = _nodeB.GetMappedPublicPort(9000);

            await Task.WhenAll(
                WaitForRhpAcceptingAsync(Host, NodeARhpPort, TimeSpan.FromSeconds(15)),
                WaitForRhpAcceptingAsync(Host, NodeBRhpPort, TimeSpan.FromSeconds(15)))
                .ConfigureAwait(false);

            IsAvailable = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Pair fixture failed to start: {ex.Message}";
            await SafeDisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task DisposeAsync()
    {
        await SafeDisposeAsync().ConfigureAwait(false);
    }

    private async Task SafeDisposeAsync()
    {
        foreach (var c in new[] { _nodeA, _nodeB })
        {
            if (c is null) continue;
            try { await c.StopAsync(); } catch { }
            try { await c.DisposeAsync(); } catch { }
        }
        if (_network is not null)
        {
            try { await _network.DeleteAsync(); } catch { }
            try { await _network.DisposeAsync(); } catch { }
        }
    }

    private IContainer BuildNode(string cfgFileName, string staticIp)
    {
        var cfgPath = Path.Combine(AppContext.BaseDirectory, "Resources", cfgFileName);
        if (!File.Exists(cfgPath))
            throw new FileNotFoundException($"Missing config: {cfgPath}");

        // Validate the Resources file actually mentions the static IP we're
        // about to assign — the IPADDRESS directive in the cfg must match
        // what xrouter sees on its interface or AXUDP routing falls over.
        var cfgText = File.ReadAllText(cfgPath);
        if (!cfgText.Contains($"IPADDRESS={staticIp}", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{cfgFileName} must declare IPADDRESS={staticIp} to match the assigned container IP.");

        return new ContainerBuilder()
            .WithImage(Image)
            .WithNetwork(_network!)
            .WithCreateParameterModifier(p =>
            {
                // Tell Docker to give this container a static IP on our
                // managed network. Testcontainers populates EndpointsConfig
                // with the network entry; we extend it with IPAM info.
                p.NetworkingConfig ??= new NetworkingConfig();
                p.NetworkingConfig.EndpointsConfig ??= new Dictionary<string, EndpointSettings>();
                if (!p.NetworkingConfig.EndpointsConfig.TryGetValue(_network!.Name, out var ep))
                {
                    ep = new EndpointSettings();
                    p.NetworkingConfig.EndpointsConfig[_network.Name] = ep;
                }
                ep.IPAMConfig = new EndpointIPAMConfig { IPv4Address = staticIp };
            })
            .WithPortBinding(9000, assignRandomHostPort: true)
            .WithResourceMapping(File.ReadAllBytes(cfgPath), "/data/XROUTER.CFG")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("xrouter version "))
            .Build();
    }

    private static async Task WaitForRhpAcceptingAsync(string host, int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var probe = new TcpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await probe.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(250).ConfigureAwait(false);
            }
        }
        throw new TimeoutException(
            $"RHP listener at {host}:{port} did not accept within {timeout}.", last);
    }
}

[CollectionDefinition(nameof(XRouterPairCollection))]
public sealed class XRouterPairCollection : ICollectionFixture<XRouterPairFixture> { }
