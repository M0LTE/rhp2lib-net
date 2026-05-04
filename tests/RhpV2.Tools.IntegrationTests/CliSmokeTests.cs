using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using RhpV2.TestSupport;
using Xunit;

namespace RhpV2.Tools.IntegrationTests;

/// <summary>
/// End-to-end smoke tests for the <c>rhp</c> CLI: each test spawns the
/// real binary, wires it up against a real RHP server (the same
/// Testcontainers-backed XRouter the library suite uses), and asserts on
/// exit code + stdout. Coverage is deliberately shallow — we want to
/// catch CLI-shape regressions (argument parsing, signal handling,
/// happy-path output), not retest what the library suite already
/// verifies.
/// </summary>
[Collection(nameof(XRouterCollection))]
public class CliSmokeTests
{
    private readonly XRouterFixture _fx;
    public CliSmokeTests(XRouterFixture fx) => _fx = fx;

    [Fact]
    public async Task Probe_Connects_And_Reports_Ok()
    {
        var r = await RhpProcess.RunAsync(
            new[] { "probe", "--host", _fx.Host, "--port", _fx.RhpPort.ToString() },
            timeout: TimeSpan.FromSeconds(15));

        Assert.True(r.ExitCode == 0, $"non-zero exit: {r}");
        Assert.Contains("TCP up", r.Stdout);
        Assert.Contains("OK", r.Stdout);
    }

    [Fact]
    public async Task Probe_Help_Prints_Usage_And_Exits_Zero()
    {
        var r = await RhpProcess.RunAsync(new[] { "probe", "--help" });

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("USAGE", r.Stdout);
        Assert.Contains("rhp probe", r.Stdout);
    }

    [Fact]
    public async Task Top_Level_Help_Lists_All_Commands()
    {
        var r = await RhpProcess.RunAsync(new[] { "--help" });

        Assert.Equal(0, r.ExitCode);
        // Every command appears in the help text.
        foreach (var cmd in new[] { "chat", "mon", "send", "probe", "serve" })
            Assert.Contains(cmd, r.Stdout);
    }

    [Fact]
    public async Task Unknown_Command_Returns_Usage_Exit_64()
    {
        var r = await RhpProcess.RunAsync(new[] { "definitely-not-a-command" });

        Assert.Equal(64, r.ExitCode);    // EX_USAGE
        Assert.Contains("unknown command", r.Stderr);
    }

    [Fact]
    public async Task Send_OneShot_Dgram_Reports_Sent()
    {
        // Single-node loopback xrouter only has port 1 (LOOPBACK). UI
        // frames go nowhere, but the API path through open(dgram) +
        // sendto returns errCode 0; rhp send prints the line.
        var r = await RhpProcess.RunAsync(
            new[]
            {
                "send",
                "--host", _fx.Host,
                "--port", _fx.RhpPort.ToString(),
                "--pfam", "ax25",
                "--radio", "1",
                "--local", "G9DUM",
                "--remote", "BEACON-1",
                "hello from rhp send",
            },
            timeout: TimeSpan.FromSeconds(15));

        Assert.True(r.ExitCode == 0, $"non-zero exit: {r}");
        Assert.Contains("sent", r.Stdout);
        Assert.Contains("BEACON-1", r.Stdout);
    }

    [Fact]
    public async Task Mon_Opens_Trace_Listener_And_Quits_On_Sigint()
    {
        // mon is long-running. Spawn it, wait for the "listening" line,
        // send SIGINT, expect a clean exit.
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var p = RhpProcess.Spawn(
            new[]
            {
                "mon",
                "--host", _fx.Host,
                "--port", _fx.RhpPort.ToString(),
                "--pfam", "ax25",
                "--radio", "1",
            },
            stdout, stderr, out var drain);

        try
        {
            // Wait up to 10s for the listener to come up.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                string snapshot;
                lock (stdout) snapshot = stdout.ToString();
                if (snapshot.Contains("listening")) break;
                await Task.Delay(100);
            }

            string finalStdout;
            lock (stdout) finalStdout = stdout.ToString();
            Assert.True(finalStdout.Contains("listening"),
                $"expected 'listening' in stdout, got:\n{finalStdout}\nstderr:\n{stderr}");

            // Polite SIGINT first; if the program doesn't tear down within
            // a few seconds escalate to Kill.
            p.CloseMainWindow();    // No-op on Linux but harmless.
            try
            {
                if (!System.Runtime.InteropServices.RuntimeInformation
                    .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    // POSIX: send SIGINT.
                    System.Diagnostics.Process.Start("kill", $"-INT {p.Id}").WaitForExit();
                }
            }
            catch { }

            using var killCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await p.WaitForExitAsync(killCts.Token); }
            catch (OperationCanceledException)
            {
                p.Kill(entireProcessTree: true);
                await p.WaitForExitAsync();
                Assert.Fail($"mon did not exit on SIGINT within 5s. stdout=\n{stdout}");
            }
        }
        finally
        {
            if (!p.HasExited) try { p.Kill(entireProcessTree: true); } catch { }
            await drain;
            p.Dispose();
        }
    }

    [Fact]
    public async Task Serve_Starts_Mock_Listener_That_Accepts_Tcp_Connections()
    {
        // serve doesn't need the xrouter container — it spins up the
        // in-process mock. Pick a fixed unprivileged port; the test
        // spawns rhp serve, waits for the "listening" line, opens a
        // TCP connection to verify the listener exists, then SIGINTs.
        const int port = 19400;

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var p = RhpProcess.Spawn(
            new[] { "serve", "--port", port.ToString() },
            stdout, stderr, out var drain);

        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                string snapshot;
                lock (stdout) snapshot = stdout.ToString();
                if (snapshot.Contains("listening on")) break;
                await Task.Delay(100);
            }

            string finalStdout;
            lock (stdout) finalStdout = stdout.ToString();
            Assert.True(finalStdout.Contains("listening on"),
                $"expected 'listening on' in stdout, got:\n{finalStdout}\nstderr:\n{stderr}");

            // Verify the mock actually accepts a TCP connection.
            using (var probe = new TcpClient())
            {
                using var probeCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                await probe.ConnectAsync("127.0.0.1", port, probeCts.Token);
                Assert.True(probe.Connected);
            }

            // SIGINT.
            try
            {
                if (!System.Runtime.InteropServices.RuntimeInformation
                    .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    System.Diagnostics.Process.Start("kill", $"-INT {p.Id}").WaitForExit();
                }
            }
            catch { }

            using var killCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await p.WaitForExitAsync(killCts.Token); }
            catch (OperationCanceledException)
            {
                p.Kill(entireProcessTree: true);
                await p.WaitForExitAsync();
                Assert.Fail($"serve did not exit on SIGINT within 5s. stdout=\n{stdout}");
            }

            string finalAll;
            lock (stdout) finalAll = stdout.ToString();
            Assert.Contains("shutting down", finalAll);
        }
        finally
        {
            if (!p.HasExited) try { p.Kill(entireProcessTree: true); } catch { }
            await drain;
            p.Dispose();
        }
    }
}
