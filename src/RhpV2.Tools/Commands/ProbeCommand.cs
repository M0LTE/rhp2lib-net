using RhpV2.Client;

namespace RhpV2.Tools.Commands;

/// <summary>
/// rhp probe — connect to an RHPv2 node, optionally authenticate, and
/// confirm the link is healthy.  Useful smoke test for new deployments.
/// </summary>
internal static class ProbeCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var opts = new CommonOptions();
        opts.Parse(args);

        if (opts.Help)
        {
            Console.WriteLine("""
            rhp probe — confirm RHPv2 connectivity

            USAGE
              rhp probe [--host H] [--port P] [--user U --pass P]

            On success prints the node's TCP endpoint and authentication state,
            then exits 0.  Any error returns a non-zero status with the message.
            """);
            return 0;
        }

        Console.WriteLine($"--> dialling {opts.Host}:{opts.Port} ...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await using var client = await RhpClient.ConnectAsync(opts.Host, opts.Port, ct);
        Console.WriteLine($"<-- TCP up in {sw.ElapsedMilliseconds} ms");

        if (!string.IsNullOrEmpty(opts.User))
        {
            sw.Restart();
            await client.AuthenticateAsync(opts.User!, opts.Pass ?? string.Empty, ct);
            Console.WriteLine($"<-- AUTH ok in {sw.ElapsedMilliseconds} ms");
        }
        else
        {
            Console.WriteLine("    (skipping AUTH; no --user supplied)");
        }

        Console.WriteLine("OK");
        return 0;
    }
}
