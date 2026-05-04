using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RhpV2.Client.Protocol;
using Xunit;
using ProtocolFamily = RhpV2.Client.Protocol.ProtocolFamily;

namespace RhpV2.Client.IntegrationTests;

/// <summary>
/// End-to-end AX.25 tests against two real xrouter containers connected
/// by AXUDP. These exercise the full data path:
/// <para>
/// <c>RhpClient → Node A RHP → Node A AX.25 L2 → AXUDP → Node B AX.25 L2
///   → Node B command processor → reply path back through the same
///   pipeline</c>.
/// </para>
/// <para>
/// SABM/UA exchange, I-frame send, I-frame receive and orderly close
/// are all real, observable on the AXUDP wire.
/// </para>
/// </summary>
[Collection(nameof(XRouterPairCollection))]
public class Ax25OverAxudpTests
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DataTimeout    = TimeSpan.FromSeconds(15);

    private readonly XRouterPairFixture _fx;
    public Ax25OverAxudpTests(XRouterPairFixture fx) => _fx = fx;

    private void RequireFixture()
    {
        Skip.IfNot(_fx.IsAvailable, _fx.UnavailableReason ?? "pair fixture unavailable");
    }

    [SkippableFact]
    public async Task BsdLifecycle_AcrossAxudp_Succeeds_And_Echoes_Banner()
    {
        RequireFixture();

        // Subscribe before opening so we don't race the async events.
        var statusEvents = new List<StatusMessage>();
        var recvEvents = new List<RecvMessage>();
        var connectedTcs = new TaskCompletionSource<StatusMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var bannerTcs = new TaskCompletionSource<RecvMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var client = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeARhpPort);
        client.StatusChanged += (_, e) =>
        {
            lock (statusEvents) statusEvents.Add(e.Message);
            if ((e.Message.Flags & (int)StatusFlags.Connected) != 0)
                connectedTcs.TrySetResult(e.Message);
        };
        client.Received += (_, e) =>
        {
            lock (recvEvents) recvEvents.Add(e.Message);
            if (!string.IsNullOrEmpty(e.Message.Data) && e.Message.Data.Contains("NODEB"))
                bannerTcs.TrySetResult(e.Message);
        };

        // BSD lifecycle: socket → bind to AXUDP port → connect to remote
        // callsign → wait for CONNECTED → send "i" (info command) → wait
        // for the banner I-frame → close.
        var handle = await client.SocketAsync(ProtocolFamily.Ax25, SocketMode.Stream);
        try
        {
            await client.BindAsync(handle, local: _fx.NodeAConsoleCallsign, port: "2");
            // The next call WOULD throw before the connectReply errCode-mirrors-
            // handle workaround was added: real xrouter sets errCode = handle on
            // success. The library now treats any connectReply with errText="Ok"
            // as success regardless of errCode, so this completes cleanly.
            await client.ConnectAsync(handle, remote: _fx.NodeBCallsign);

            var connected = await connectedTcs.Task.WaitAsync(ConnectTimeout);
            Assert.Equal(handle, connected.Handle);
            Assert.True((connected.Flags & (int)StatusFlags.Connected) != 0);

            var sendReply = await client.SendOnHandleAsync(handle, "i\r");
            Assert.Equal(0, sendReply.ErrCode);
            Assert.Equal(handle, sendReply.Handle);

            var banner = await bannerTcs.Task.WaitAsync(DataTimeout);
            Assert.Equal(handle, banner.Handle);
            Assert.Contains("NODEB", banner.Data);
        }
        finally
        {
            try { await client.CloseAsync(handle); } catch { /* tolerate races */ }
        }
    }

    [SkippableFact]
    public async Task ConnectReply_From_Real_Xrouter_Has_ErrCode_Equal_To_Handle()
    {
        // Pin the actual wire-level quirk that the library now tolerates.
        // We bypass RhpClient and look at the raw connectReply bytes so a
        // future xrouter release that fixes the bug will surface as a
        // failing assertion (and we can drop the workaround).
        RequireFixture();

        using var tcp = new TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await tcp.ConnectAsync(_fx.Host, _fx.NodeARhpPort, cts.Token);
        await using var stream = tcp.GetStream();

        await SendFrame(stream, new JsonObject
        {
            ["type"] = "socket",
            ["id"] = 1,
            ["pfam"] = "ax25",
            ["mode"] = "stream",
        });
        var socketReply = await ReadObject(stream, cts.Token);
        var handle = socketReply["handle"]!.GetValue<int>();

        await SendFrame(stream, new JsonObject
        {
            ["type"] = "bind",
            ["id"] = 2,
            ["handle"] = handle,
            ["local"] = _fx.NodeAConsoleCallsign,
            ["port"] = "1",
        });
        await ReadObject(stream, cts.Token);

        await SendFrame(stream, new JsonObject
        {
            ["type"] = "connect",
            ["id"] = 3,
            ["handle"] = handle,
            ["remote"] = _fx.NodeACallsign, // any well-formed callsign — local loopback
        });
        var connectReply = await ReadObject(stream, cts.Token);

        Assert.Equal("connectReply", connectReply["type"]!.GetValue<string>());
        Assert.Equal("Ok", connectReply["errText"]!.GetValue<string>());
        Assert.Equal(handle, connectReply["handle"]!.GetValue<int>());
        // The bug: errCode mirrors handle on success rather than being 0.
        Assert.Equal(handle, connectReply["errCode"]!.GetValue<int>());
    }

    private static async Task SendFrame(NetworkStream stream, JsonObject obj)
    {
        var bytes = Encoding.UTF8.GetBytes(obj.ToJsonString());
        await RhpFraming.WriteFrameAsync(stream, bytes);
    }

    private static async Task<JsonObject> ReadObject(NetworkStream stream, CancellationToken ct)
    {
        var bytes = await RhpFraming.ReadFrameAsync(stream, ct)
            ?? throw new InvalidOperationException("server closed unexpectedly.");
        return (JsonObject)JsonNode.Parse(bytes)!;
    }
}
