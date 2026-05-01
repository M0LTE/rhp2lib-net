using RhpV2.Client.Testing;

namespace RhpV2.Tools.Commands;

/// <summary>
/// rhp serve — runs the in-process MockRhpServer.  Lets developers point
/// their RHP-aware applications at a local node for offline testing.
/// </summary>
internal static class ServeCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        int port = 9000;
        string? user = null;
        string? pass = null;
        bool help = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port": port = int.Parse(args[++i]); break;
                case "--user": user = args[++i]; break;
                case "--pass": pass = args[++i]; break;
                case "-h":
                case "--help": help = true; break;
            }
        }

        if (help)
        {
            Console.WriteLine("""
            rhp serve — local mock RHPv2 server (developer harness)

            USAGE
              rhp serve [--port 9000] [--user U --pass P]

            Starts a loopback-only MockRhpServer that responds to OPEN, SEND,
            CLOSE etc. with sensible defaults.  Useful for CI, demos, and
            poking at the protocol without a real radio.
            """);
            return 0;
        }

        await using var server = new MockRhpServer(port);
        if (user is not null)
        {
            server.RequireAuth = true;
            server.Credentials = (user, pass ?? string.Empty);
        }
        server.Start();

        Console.WriteLine($"mock RHPv2 server listening on {server.Endpoint}");
        if (user is not null) Console.WriteLine($"AUTH required as user '{user}'");
        Console.WriteLine("Ctrl-C to quit.");

        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
        Console.WriteLine("shutting down.");
        return 0;
    }
}
