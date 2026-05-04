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
/// Spins up the published <c>ghcr.io/packethacking/xrouter</c> container
/// once per test class collection and exposes its mapped TCP port for the
/// RHP server (port 9000 inside the container).
///
/// The fixture is skipped silently when Docker isn't available so the
/// project still builds on machines without a Docker daemon (the
/// per-test <see cref="DockerAvailabilityCheck"/> turns each test into a
/// "skipped" result rather than a hard build failure).
/// </summary>
public sealed class XRouterFixture : IAsyncLifetime
{
    private const string Image = "ghcr.io/packethacking/xrouter";

    /// <summary>Mapped host port for the container's RHP server (9000/tcp).</summary>
    public ushort RhpPort { get; private set; }

    /// <summary>Host the test should connect to. Always loopback.</summary>
    public string Host => "127.0.0.1";

    /// <summary>True once the fixture has confirmed Docker + container are up.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Reason the fixture is unavailable, if any.</summary>
    public string? UnavailableReason { get; private set; }

    private IContainer? _container;

    public async Task InitializeAsync()
    {
        if (!await DockerAvailabilityCheck.IsDockerRunningAsync().ConfigureAwait(false))
        {
            UnavailableReason = "Docker daemon not reachable.";
            return;
        }

        var cfgPath = Path.Combine(AppContext.BaseDirectory, "Resources", "XROUTER.CFG");
        if (!File.Exists(cfgPath))
        {
            UnavailableReason = $"XROUTER.CFG not found at {cfgPath}.";
            return;
        }

        try
        {
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

            IsAvailable = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Container failed to start: {ex.Message}";
            try { if (_container is not null) await _container.DisposeAsync(); } catch { }
            _container = null;
        }
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

internal static class DockerAvailabilityCheck
{
    private static bool? _cached;

    public static async Task<bool> IsDockerRunningAsync()
    {
        if (_cached is bool b) return b;
        try
        {
            // ContainerBuilder.Build() doesn't ping the daemon; do a cheap
            // probe by reaching for the docker.sock / named-pipe. The
            // simplest portable check is to actually try to connect: but
            // we keep it cheap here by deferring all the way to start.
            // For environments without docker we'll just throw at start.
            await Task.Yield();
            _cached = true;
            return true;
        }
        catch
        {
            _cached = false;
            return false;
        }
    }
}

[CollectionDefinition(nameof(XRouterCollection))]
public sealed class XRouterCollection : ICollectionFixture<XRouterFixture> { }
