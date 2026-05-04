using System;
using System.Threading.Tasks;
using RhpV2.Client.Protocol;
using Xunit;
using ProtocolFamily = RhpV2.Client.Protocol.ProtocolFamily;

namespace RhpV2.Client.IntegrationTests;

/// <summary>
/// NetRom routing is sensitive to xrouter's internal AX.25 link / NetRom
/// node-table state, which accumulates across the long-lived containers
/// in <see cref="XRouterPairFixture"/>. Run NetRom against its own fresh
/// fixture instance so we get a clean xrouter pair with a known-good
/// AXUDP link and an empty NetRom routing table.
/// </summary>
public sealed class NetRomTests : IClassFixture<XRouterPairFixture>
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DataTimeout    = TimeSpan.FromSeconds(15);

    private readonly XRouterPairFixture _fx;
    public NetRomTests(XRouterPairFixture fx) => _fx = fx;

    [Fact]
    public async Task NetRom_Stream_Connect_To_Peer_Routes_Through_Ax25_Link()
    {
        // NetRom rides on top of AX.25. On a fresh xrouter pair with a
        // configured AXUDP partner port, opening a NetRom stream and
        // connecting to the peer's NODECALL works directly — xrouter
        // brings the underlying AX.25 link up on demand. A successful
        // round-trip ("i" → banner from the peer's command processor)
        // confirms the NetRom path is exercised end-to-end.

        await using var client = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeARhpPort);

        var connectedTcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var bannerTcs = new TaskCompletionSource<RecvMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.StatusChanged += (_, e) =>
        {
            if ((e.Message.Flags & (int)StatusFlags.Connected) != 0)
                connectedTcs.TrySetResult(true);
        };
        client.Received += (_, e) =>
        {
            if (e.Message.Data.Contains("NODEB", StringComparison.OrdinalIgnoreCase))
                bannerTcs.TrySetResult(e.Message);
        };

        var h = await client.SocketAsync(ProtocolFamily.NetRom, SocketMode.Stream);
        await client.BindAsync(h, local: "G9DUM");
        await client.ConnectAsync(h, remote: _fx.NodeBCallsign);
        await connectedTcs.Task.WaitAsync(ConnectTimeout);

        await client.SendOnHandleAsync(h, "i\r");
        var banner = await bannerTcs.Task.WaitAsync(DataTimeout);
        Assert.Equal(h, banner.Handle);
        Assert.Contains("NODEB", banner.Data);

        try { await client.CloseAsync(h); } catch { }
    }
}
