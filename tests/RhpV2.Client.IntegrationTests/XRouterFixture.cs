using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace RhpV2.Client.IntegrationTests;

/// <summary>
/// Spins up a single <c>ghcr.io/packethacking/xrouter</c> container per
/// test class collection and exposes its mapped TCP port for the RHP
/// server (port 9000 inside the container).
///
/// Requires a running Docker daemon. If <c>InitializeAsync</c> fails to
/// start the container, the failure propagates and every test in the
/// class fails loudly — there's no silent skip.
/// </summary>
public sealed class XRouterFixture : IAsyncLifetime
{
    private const string Image = "ghcr.io/packethacking/xrouter";

    /// <summary>Mapped host port for the container's RHP server (9000/tcp).</summary>
    public ushort RhpPort { get; private set; }

    /// <summary>Host the test should connect to. Always loopback.</summary>
    public string Host => "127.0.0.1";

    private IContainer? _container;

    public async Task InitializeAsync()
    {
        var cfgPath = Path.Combine(AppContext.BaseDirectory, "Resources", "XROUTER.CFG");
        if (!File.Exists(cfgPath))
            throw new FileNotFoundException(
                $"XROUTER.CFG not found at {cfgPath}.", cfgPath);

        _container = new ContainerBuilder()
            .WithImage(Image)
            .WithPortBinding(9000, assignRandomHostPort: true)
            .WithResourceMapping(
                File.ReadAllBytes(cfgPath),
                "/data/XROUTER.CFG")
            // The image's entrypoint tails LOG/*.TXT; wait for the
            // canonical "started" line before we let tests run.
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("xrouter version "))
            .Build();

        await _container.StartAsync().ConfigureAwait(false);
        RhpPort = _container.GetMappedPublicPort(9000);

        // The wait strategy fires as soon as xrouter starts; the RHP
        // listener follows a moment later. Poll briefly so tests don't
        // race the server.
        await WaitForRhpAcceptingAsync(Host, RhpPort, TimeSpan.FromSeconds(15))
            .ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            try { await _container.StopAsync(); } catch { }
            try { await _container.DisposeAsync(); } catch { }
        }
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

[CollectionDefinition(nameof(XRouterCollection))]
public sealed class XRouterCollection : ICollectionFixture<XRouterFixture> { }
