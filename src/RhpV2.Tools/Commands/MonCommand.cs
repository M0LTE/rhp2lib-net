using RhpV2.Client;
using RhpV2.Client.Protocol;

namespace RhpV2.Tools.Commands;

/// <summary>
/// rhp mon — open a TRACE-mode socket on a radio port and continuously
/// dump every frame the radio sees, with decoded headers.
///
/// Equivalent to a packet-radio "monitor" view in classic terminals.
/// </summary>
internal static class MonCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var opts = new CommonOptions { Mode = SocketMode.Trace };
        var rest = opts.Parse(args);
        bool incoming = true, outgoing = true, supervisory = true;
        bool hex = false;

        foreach (var arg in rest)
        {
            switch (arg)
            {
                case "--no-incoming":   incoming = false; break;
                case "--no-outgoing":   outgoing = false; break;
                case "--no-supervisory":supervisory = false; break;
                case "--hex":           hex = true; break;
            }
        }

        if (opts.Help || string.IsNullOrEmpty(opts.Radio))
        {
            Console.WriteLine("""
            rhp mon — trace-mode frame monitor

            USAGE
              rhp mon --radio <port> [--pfam ax25|netrom] [--no-incoming]
                      [--no-outgoing] [--no-supervisory] [--hex]
                      [--host H] [--port P] [--user U --pass P]

            Opens a TRACE-mode socket on the requested XRouter radio port and
            prints every frame as it is heard.  --hex dumps the payload as
            hex; otherwise it's printed as printable ASCII.
            """);
            return string.IsNullOrEmpty(opts.Radio) ? 64 : 0;
        }

        await using var client = await opts.ConnectAndAuthAsync(ct);

        var flags = OpenFlags.Passive;
        if (incoming)    flags |= OpenFlags.TraceIncoming;
        if (outgoing)    flags |= OpenFlags.TraceOutgoing;
        if (supervisory) flags |= OpenFlags.TraceSupervisory;

        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += (_, _) => disconnected.TrySetResult();
        client.Received += (_, e) => PrintTraceFrame(e.Message, hex);
        client.UnknownReceived += (_, e) =>
            Console.WriteLine($"    ?? unrecognised frame type='{e.Message.Type}'");

        Console.WriteLine($"--> opening TRACE on radio '{opts.Radio}' (flags={(int)flags:X})");
        var handle = await client.OpenAsync(opts.Pfam, SocketMode.Trace,
            port: opts.Radio, local: opts.Local, flags: flags, ct: ct);
        Console.WriteLine($"<-- handle {handle}, listening (Ctrl-C to quit)\n");

        await Task.WhenAny(disconnected.Task, Task.Delay(Timeout.Infinite, ct))
            .ConfigureAwait(false);
        return 0;
    }

    private static void PrintTraceFrame(RecvMessage m, bool hex)
    {
        var dir = (m.Action ?? "") switch { "sent" => "TX", "rcvd" => "RX", _ => "??" };
        var src = m.Srce ?? "?";
        var dst = m.Dest ?? "?";
        var type = m.FrameType ?? "I";
        var pf = (m.Pf ?? "") == "F" ? "F" : "P";
        var cr = (m.Cr ?? "") == "C" ? "C" : "R";

        var prefix = $"{DateTime.Now:HH:mm:ss} {dir} {src,-9}->{dst,-9} {type,-3} {cr}{pf}";

        var bytes = RhpDataEncoding.FromWireString(m.Data ?? string.Empty);
        if (bytes.Length == 0)
        {
            Console.WriteLine(prefix);
            return;
        }
        if (hex)
        {
            Console.WriteLine($"{prefix} ({bytes.Length}b):");
            HexDump(bytes);
        }
        else
        {
            Console.WriteLine($"{prefix} {ToPrintable(bytes)}");
        }
    }

    private static string ToPrintable(byte[] data)
    {
        var sb = new System.Text.StringBuilder(data.Length);
        foreach (var b in data)
        {
            if (b == 0x0D) sb.Append("\\r");
            else if (b == 0x0A) sb.Append("\\n");
            else if (b is >= 0x20 and < 0x7F) sb.Append((char)b);
            else sb.Append('.');
        }
        return sb.ToString();
    }

    private static void HexDump(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i += 16)
        {
            var line = data.Slice(i, Math.Min(16, data.Length - i));
            var hex = string.Join(' ', line.ToArray().Select(b => b.ToString("X2")));
            var asc = ToPrintable(line.ToArray());
            Console.WriteLine($"  {i:X4}  {hex,-47}  {asc}");
        }
    }
}
