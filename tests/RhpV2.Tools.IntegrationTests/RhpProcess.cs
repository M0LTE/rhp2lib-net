using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RhpV2.Tools.IntegrationTests;

/// <summary>
/// Spawns the actual <c>rhp</c> binary (via <c>dotnet rhp.dll</c>) so each
/// test exercises the real command-line entry point — argument parsing,
/// exit codes, stdout / stderr, the lot — not just the library APIs the
/// CLI is built on top of.
///
/// The binary is resolved relative to this test assembly's bin directory
/// — both projects build under the same configuration so the build that
/// produced this test assembly also produced the matching <c>rhp.dll</c>.
/// </summary>
internal static class RhpProcess
{
    /// <summary>
    /// Path to the <c>rhp.dll</c> emitted by the <c>RhpV2.Tools</c>
    /// project, in the same configuration as this test assembly.
    /// </summary>
    public static string RhpDllPath { get; } = ResolveRhpDll();

    private static string ResolveRhpDll()
    {
        // tests/RhpV2.Tools.IntegrationTests/bin/<Cfg>/<Tfm>/...
        // → walk up to the repo root, then back down into Tools/bin.
        var here = AppContext.BaseDirectory;
        // Ascend to repo root: directory containing RhpV2.slnx.
        var dir = new DirectoryInfo(here);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RhpV2.slnx")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException(
                $"could not find repo root (looking for RhpV2.slnx) starting from {here}");

        // Use the same TFM/Configuration as this test assembly.
        var tfm = Path.GetFileName(Path.TrimEndingDirectorySeparator(here));
        var cfg = Path.GetFileName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(here))!);

        var path = Path.Combine(
            dir.FullName, "src", "RhpV2.Tools", "bin", cfg, tfm, "rhp.dll");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"rhp.dll not found at expected path {path}. " +
                $"ProjectReference should have built it; check Tools project output.");
        return path;
    }

    /// <summary>
    /// Run <c>rhp</c> with the given args, wait for it to exit (or
    /// <paramref name="timeout"/>), and return its exit code + captured
    /// stdout / stderr.
    /// </summary>
    public static async Task<RhpResult> RunAsync(
        string[] args,
        TimeSpan? timeout = null,
        string? stdin = null,
        CancellationToken ct = default)
    {
        var psi = BuildStartInfo(args);
        if (stdin is not null)
            psi.RedirectStandardInput = true;

        var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Process.Start returned null.");

        if (stdin is not null)
        {
            await p.StandardInput.WriteAsync(stdin);
            p.StandardInput.Close();
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutTask = ReadAllAsync(p.StandardOutput, stdout, ct);
        var stderrTask = ReadAllAsync(p.StandardError, stderr, ct);

        var deadline = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(deadline);

        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            await Task.WhenAll(stdoutTask, stderrTask);
            throw new TimeoutException(
                $"rhp {string.Join(' ', args)} did not exit within {deadline}.\n" +
                $"--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        }

        await Task.WhenAll(stdoutTask, stderrTask);
        return new RhpResult(p.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>
    /// Spawn <c>rhp</c> and return the running <see cref="Process"/>
    /// without waiting. The caller drives lifecycle (e.g. wait for a
    /// stdout marker, then send Ctrl-C / Kill). Stdout / stderr are
    /// captured into the supplied <see cref="StringBuilder"/>s.
    /// Set <paramref name="redirectStdin"/> true to drive the
    /// process's stdin via <see cref="Process.StandardInput"/> — for
    /// interactive commands like <c>chat</c>.
    /// </summary>
    public static Process Spawn(
        string[] args,
        StringBuilder stdout,
        StringBuilder stderr,
        out Task drainTask,
        bool redirectStdin = false)
    {
        var psi = BuildStartInfo(args);
        if (redirectStdin) psi.RedirectStandardInput = true;
        var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Process.Start returned null.");
        drainTask = Task.WhenAll(
            ReadAllAsync(p.StandardOutput, stdout, default),
            ReadAllAsync(p.StandardError, stderr, default));
        return p;
    }

    /// <summary>
    /// Wait until <paramref name="predicate"/> returns true on the
    /// captured stdout text, polling every 100 ms up to
    /// <paramref name="timeout"/>. Throws <see cref="TimeoutException"/>
    /// otherwise, including a stdout snapshot for debugging.
    /// </summary>
    public static async Task WaitForStdoutAsync(
        StringBuilder stdout,
        Func<string, bool> predicate,
        TimeSpan timeout,
        string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            string snapshot;
            lock (stdout) snapshot = stdout.ToString();
            if (predicate(snapshot)) return;
            await Task.Delay(100);
        }
        string final;
        lock (stdout) final = stdout.ToString();
        throw new TimeoutException(
            $"timed out waiting for: {description}\n--- stdout so far ---\n{final}");
    }

    private static ProcessStartInfo BuildStartInfo(string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(RhpDllPath);
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    private static async Task ReadAllAsync(
        StreamReader reader, StringBuilder sink, CancellationToken ct)
    {
        var buf = new char[4096];
        while (!ct.IsCancellationRequested)
        {
            int n;
            try { n = await reader.ReadAsync(buf, ct); }
            catch { break; }
            if (n <= 0) break;
            lock (sink) sink.Append(buf, 0, n);
        }
    }
}

internal sealed record RhpResult(int ExitCode, string Stdout, string Stderr)
{
    public override string ToString() =>
        $"exit={ExitCode}\n--- stdout ---\n{Stdout}--- stderr ---\n{Stderr}";
}
