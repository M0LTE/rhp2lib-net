using RhpV2.Client;
using RhpV2.Client.Protocol;

namespace RhpV2.Tools;

/// <summary>
/// Shared connection options for every CLI command.  Parsed in a tiny
/// hand-rolled fashion (we deliberately avoid taking on a dependency just
/// for argument parsing).
/// </summary>
internal sealed class CommonOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = RhpClient.DefaultPort;
    public string? User { get; set; }
    public string? Pass { get; set; }

    public string Pfam { get; set; } = ProtocolFamily.Ax25;
    public string Mode { get; set; } = SocketMode.Stream;
    public string? Radio { get; set; }
    public string? Local { get; set; }
    public string? Remote { get; set; }

    public bool Help { get; set; }

    /// <summary>Parses the standard option set; returns leftover positional args.</summary>
    public List<string> Parse(string[] args)
    {
        var positional = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (a)
            {
                case "--host":   Host   = Next() ?? Host; break;
                case "--port":   Port   = int.Parse(Next() ?? Port.ToString()); break;
                case "--user":   User   = Next(); break;
                case "--pass":   Pass   = Next(); break;
                case "--pfam":   Pfam   = Next() ?? Pfam; break;
                case "--mode":   Mode   = Next() ?? Mode; break;
                case "--radio":
                case "--rport":
                case "--xport":  Radio  = Next(); break;
                case "--local":  Local  = Next(); break;
                case "--remote": Remote = Next(); break;
                case "-h":
                case "--help":   Help = true; break;
                default:         positional.Add(a); break;
            }
        }
        return positional;
    }

    public async Task<RhpClient> ConnectAndAuthAsync(CancellationToken ct)
    {
        var client = await RhpClient.ConnectAsync(Host, Port, ct).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrEmpty(User))
                await client.AuthenticateAsync(User!, Pass ?? string.Empty, ct).ConfigureAwait(false);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
        return client;
    }
}
