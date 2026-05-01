using System.Text;
using RhpV2.Client.Protocol;

namespace RhpV2.Tools.Commands;

/// <summary>
/// rhp send — fire a one-shot UI/datagram frame and exit.  Useful for
/// scripting beacons, APRS-style messages, or bench tests against a mock.
/// </summary>
internal static class SendCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var opts = new CommonOptions { Mode = SocketMode.Dgram };
        var rest = opts.Parse(args);

        string? message = null;
        bool fromStdin = false;
        bool fromHex = false;
        foreach (var a in rest)
        {
            if (a == "-")            fromStdin = true;
            else if (a == "--hex")   fromHex = true;
            else                     message ??= a;
        }

        if (opts.Help || (string.IsNullOrEmpty(opts.Radio) && opts.Pfam != ProtocolFamily.Inet))
        {
            Console.WriteLine("""
            rhp send — one-shot datagram / UI-frame transmitter

            USAGE
              rhp send --pfam ax25|netrom --radio <port> --local <call>
                       --remote <call> "message text"
              rhp send ... --hex "DEADBEEF01"
              rhp send ... -                # read payload from stdin

            Sends one packet and exits.  For ax25 this transmits a UI frame.
            For netrom it transmits a NETROM-DGRAM packet.
            """);
            return string.IsNullOrEmpty(opts.Radio) ? 64 : 0;
        }

        byte[] payload;
        if (fromStdin)
        {
            using var ms = new MemoryStream();
            await Console.OpenStandardInput().CopyToAsync(ms, ct);
            payload = ms.ToArray();
        }
        else if (fromHex && message is not null)
        {
            payload = ParseHex(message);
        }
        else
        {
            payload = Encoding.UTF8.GetBytes(message ?? string.Empty);
        }

        await using var client = await opts.ConnectAndAuthAsync(ct);
        var handle = await client.OpenAsync(
            opts.Pfam, opts.Mode,
            port: opts.Radio, local: opts.Local, flags: OpenFlags.Passive, ct: ct);
        try
        {
            var reply = await client.SendToAsync(
                handle,
                RhpDataEncoding.ToWireString(payload),
                port: opts.Radio,
                local: opts.Local,
                remote: opts.Remote,
                ct: ct);
            Console.WriteLine($"sent {payload.Length} bytes to {opts.Remote} via radio {opts.Radio} (errcode={reply.ErrCode})");
            return reply.ErrCode == 0 ? 0 : 1;
        }
        finally
        {
            try { await client.CloseAsync(handle, ct); }
            catch { /* best effort */ }
        }
    }

    private static byte[] ParseHex(string s)
    {
        s = s.Replace(" ", "").Replace("-", "").Replace(":", "");
        if (s.Length % 2 != 0) throw new FormatException("hex payload has odd length.");
        var bytes = new byte[s.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
        return bytes;
    }
}
