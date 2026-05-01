using RhpV2.Tools.Commands;

namespace RhpV2.Tools;

/// <summary>
/// Top-level CLI dispatcher.  All sub-commands share a common host/port/auth
/// vocabulary so the experience is consistent across tools.
/// </summary>
internal static class Program
{
    private static readonly Dictionary<string, Func<string[], CancellationToken, Task<int>>> Commands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["chat"]    = ChatCommand.RunAsync,
            ["mon"]     = MonCommand.RunAsync,
            ["monitor"] = MonCommand.RunAsync,
            ["send"]    = SendCommand.RunAsync,
            ["probe"]   = ProbeCommand.RunAsync,
            ["serve"]   = ServeCommand.RunAsync,
        };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintTopLevelHelp();
            return 0;
        }

        if (!Commands.TryGetValue(args[0], out var command))
        {
            await Console.Error.WriteLineAsync($"rhp: unknown command '{args[0]}'.");
            PrintTopLevelHelp();
            return 64; // EX_USAGE
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            return await command(args[1..], cts.Token);
        }
        catch (OperationCanceledException)
        {
            return 130; // SIGINT convention
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"rhp: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void PrintTopLevelHelp()
    {
        Console.WriteLine("""
        rhp — RHPv2 packet-radio toolkit

        USAGE
          rhp <command> [options]

        COMMANDS
          chat     Open an interactive AX.25 / NetRom STREAM session.
          mon      Monitor a radio port in TRACE mode (decoded headers + payload).
          send     Transmit a one-shot UI/datagram frame and exit.
          probe    Connect to an RHP node, run AUTH (if requested), report status.
          serve    Run a local mock RHPv2 server (developer harness).

        Common options for chat/mon/send/probe:
          --host <host>        RHP host (default 127.0.0.1)
          --port <port>        RHP TCP port (default 9000)
          --user <user>        AUTH username (optional)
          --pass <pass>        AUTH password (optional)
          --pfam <family>      ax25 | netrom | inet | unix (default ax25)
          --radio <port>       XRouter radio port id (e.g. "1")
          --local <call>       Local callsign / address
          --remote <call>      Remote callsign / address
          --mode <mode>        stream | dgram | trace | raw (per command default)

        See `rhp <command> --help` for command-specific help.
        """);
    }
}
