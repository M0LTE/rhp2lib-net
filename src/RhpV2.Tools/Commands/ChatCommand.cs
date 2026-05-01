using System.Text;
using RhpV2.Client;
using RhpV2.Client.Protocol;

namespace RhpV2.Tools.Commands;

/// <summary>
/// rhp chat — interactive STREAM-mode chat over AX.25 or NetRom.
///
/// Lines typed at the terminal are sent to the remote with CR appended
/// (the convention for AX.25 keyboard sessions).  RECV frames from the
/// remote are printed as they arrive.  Ctrl-D / Ctrl-C closes the link.
/// </summary>
internal static class ChatCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var opts = new CommonOptions { Mode = SocketMode.Stream };
        opts.Parse(args);

        if (opts.Help || string.IsNullOrEmpty(opts.Remote) || string.IsNullOrEmpty(opts.Local))
        {
            Console.WriteLine("""
            rhp chat — interactive AX.25 / NetRom STREAM session

            USAGE
              rhp chat --pfam ax25|netrom --radio <port>
                       --local <mycall> --remote <theircall>
                       [--host H] [--port P] [--user U --pass P]

            Type a line and Enter to transmit.  Incoming RECV frames are
            shown as they arrive.  Ctrl-D ends the session.
            """);
            return string.IsNullOrEmpty(opts.Remote) || string.IsNullOrEmpty(opts.Local) ? 64 : 0;
        }

        await using var client = await opts.ConnectAndAuthAsync(ct);

        var stopReason = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += (_, _) => stopReason.TrySetResult("transport closed");
        client.Closed += (_, _) => stopReason.TrySetResult("remote closed link");
        client.StatusChanged += (_, e) =>
        {
            var f = (StatusFlags)(e.Message.Flags ?? 0);
            Console.WriteLine($"[status] handle={e.Message.Handle} {f}");
            if (!f.HasFlag(StatusFlags.Connected) && f != StatusFlags.None)
                stopReason.TrySetResult("downlink lost");
        };
        client.Received += (_, e) =>
        {
            var data = RhpDataEncoding.FromWireString(e.Message.Data);
            // Translate CR to newline for terminal display; preserve LF.
            var s = Encoding.UTF8.GetString(data).Replace("\r", "\n");
            Console.Write(s);
        };

        Console.WriteLine($"--> connecting to {opts.Remote} via {opts.Pfam}/{opts.Radio} (local={opts.Local})");
        var handle = await client.OpenAsync(
            opts.Pfam, SocketMode.Stream,
            port: opts.Radio, local: opts.Local, remote: opts.Remote,
            flags: OpenFlags.Active, ct: ct);
        Console.WriteLine($"<-- handle {handle}, type messages and press Enter (Ctrl-D to quit)\n");

        // Spawn a dedicated reader on the input.  We can't cancel a Console.ReadLine
        // cleanly so we let it finish on its own when stdin closes.
        var inputTask = Task.Run(async () =>
        {
            using var stdin = new StreamReader(Console.OpenStandardInput(), Console.InputEncoding);
            while (!ct.IsCancellationRequested)
            {
                var line = await stdin.ReadLineAsync(ct);
                if (line is null)
                {
                    stopReason.TrySetResult("stdin closed");
                    break;
                }
                try
                {
                    await client.SendOnHandleAsync(handle, line + "\r", ct);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[send error] {ex.Message}");
                    stopReason.TrySetResult("send failed");
                    break;
                }
            }
        }, ct);

        var stop = await Task.WhenAny(stopReason.Task, Task.Delay(Timeout.Infinite, ct));
        var reason = stop == stopReason.Task ? await stopReason.Task : "cancelled";
        Console.WriteLine($"\n--> {reason}, closing link.");

        try { await client.CloseAsync(handle, CancellationToken.None); }
        catch { /* best effort */ }

        return 0;
    }
}
