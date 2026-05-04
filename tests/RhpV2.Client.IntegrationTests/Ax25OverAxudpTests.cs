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
    public async Task QueryStatusAsync_Returns_Connected_Flag_On_Active_Handle()
    {
        // Real xrouter responds to a successful status query with a
        // status NOTIFICATION (no id), not a statusReply. The library
        // now races the notification path against the error path; this
        // pins that the success branch returns CONNECTED for an
        // established AX.25 stream.
        RequireFixture();

        const string ListenerCall = "G9DUM-7";
        const string CallerCall   = "G8PZT-7";

        await using var nodeA = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeARhpPort);
        await using var nodeB = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeBRhpPort);

        var listener = await nodeA.SocketAsync(ProtocolFamily.Ax25, SocketMode.Stream);
        await nodeA.BindAsync(listener, local: ListenerCall, port: "2");
        await nodeA.ListenAsync(listener);

        var connectedTcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        nodeB.StatusChanged += (_, e) =>
        {
            if ((e.Message.Flags & (int)StatusFlags.Connected) != 0)
                connectedTcs.TrySetResult(true);
        };

        var caller = await nodeB.SocketAsync(ProtocolFamily.Ax25, SocketMode.Stream);
        await nodeB.BindAsync(caller, local: CallerCall, port: "2");
        await nodeB.ConnectAsync(caller, remote: ListenerCall);
        await connectedTcs.Task.WaitAsync(ConnectTimeout);

        // Now query — success path returns the current flags via the
        // async notification.
        var flags = await nodeB.QueryStatusAsync(caller, responseTimeout: TimeSpan.FromSeconds(5));
        Assert.True((flags & StatusFlags.Connected) != 0,
            $"expected Connected, got {flags}");

        try { await nodeB.CloseAsync(caller); } catch { }
        try { await nodeA.CloseAsync(listener); } catch { }
    }

    [SkippableFact]
    public async Task Binary_Bytes_Round_Trip_Via_Dgram_Through_Real_Xrouter()
    {
        // The library encodes binary payloads as Latin-1 in the JSON
        // `data` field; STJ then promotes high bytes (0x80–0xFF) to
        // 2-byte UTF-8 sequences on the wire. Verify a payload covering
        // null, low control chars, and the high byte boundary survives
        // a real cross-AXUDP UI-frame round trip byte-for-byte.
        RequireFixture();

        const string AReceiverCall = "G9DUM-6";
        const string BSenderCall   = "G8PZT-6";
        var binary = new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xAA, 0xFE, 0xFF, 0x0D, 0x0A };

        await using var nodeA = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeARhpPort);
        await using var nodeB = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeBRhpPort);

        var recvTcs = new TaskCompletionSource<RecvMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        nodeA.Received += (_, e) =>
        {
            if (e.Message.Local == AReceiverCall) recvTcs.TrySetResult(e.Message);
        };

        var receiver = await nodeA.SocketAsync(ProtocolFamily.Ax25, SocketMode.Dgram);
        await nodeA.BindAsync(receiver, local: AReceiverCall, port: "2");

        var sender = await nodeB.SocketAsync(ProtocolFamily.Ax25, SocketMode.Dgram);
        await nodeB.BindAsync(sender, local: BSenderCall, port: "2");
        await nodeB.SendToAsync(sender,
            data: RhpDataEncoding.ToWireString(binary),
            port: "2",
            local: BSenderCall,
            remote: AReceiverCall);

        var recv = await recvTcs.Task.WaitAsync(DataTimeout);
        var got = RhpDataEncoding.FromWireString(recv.Data);
        Assert.Equal(binary, got);

        try { await nodeA.CloseAsync(receiver); } catch { }
        try { await nodeB.CloseAsync(sender); } catch { }
    }

    [SkippableFact]
    public async Task Listen_Twice_On_Same_Local_Returns_DuplicateSocket()
    {
        // bind succeeds twice for the same callsign+port pair, but
        // listen on the second one fails with errCode 9 ("Duplicate
        // socket") — the spec error code is hit for real here.
        RequireFixture();

        await using var client = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeARhpPort);
        var h1 = await client.SocketAsync(ProtocolFamily.Ax25, SocketMode.Stream);
        var h2 = await client.SocketAsync(ProtocolFamily.Ax25, SocketMode.Stream);
        try
        {
            await client.BindAsync(h1, local: "G9DUM-D", port: "1");
            await client.BindAsync(h2, local: "G9DUM-D", port: "1");
            await client.ListenAsync(h1);

            var ex = await Assert.ThrowsAsync<RhpServerException>(
                async () => await client.ListenAsync(h2));
            Assert.Equal(RhpErrorCode.DuplicateSocket, ex.ErrorCode);
        }
        finally
        {
            try { await client.CloseAsync(h1); } catch { }
            try { await client.CloseAsync(h2); } catch { }
        }
    }

    [SkippableFact]
    public async Task Inet_Stream_Bind_And_Connect_To_Local_Service_Succeeds()
    {
        // pfam=inet exercises a different code path inside xrouter
        // (the embedded TCP/IP stack). Connect to xrouter's own HTTP
        // server on 127.0.0.1:8086 and verify the connectReply +
        // CONNECTED status path. Also pins that the connectReply
        // errCode-mirrors-handle quirk is family-agnostic.
        RequireFixture();

        await using var client = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeARhpPort);

        var connectedTcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.StatusChanged += (_, e) =>
        {
            if ((e.Message.Flags & (int)StatusFlags.Connected) != 0)
                connectedTcs.TrySetResult(true);
        };

        var h = await client.SocketAsync(ProtocolFamily.Inet, SocketMode.Stream);
        await client.BindAsync(h, local: "127.0.0.1:0");
        await client.ConnectAsync(h, remote: "127.0.0.1:8086");

        await connectedTcs.Task.WaitAsync(ConnectTimeout);
        try { await client.CloseAsync(h); } catch { }
    }

    [SkippableFact]
    public async Task SendReply_Status_Surfaces_Busy_Flag_On_Large_Loopback_Send()
    {
        // The `status` field in `sendReply` carries the live link
        // state at reply time. A few-KB send through the loopback
        // port to the node's own command processor reliably trips
        // the BUSY bit. Pin that the library's StatusFlags enum
        // surfaces it correctly.
        //
        // Stays safely under the ~8 KB per-`send` ceiling
        // (xrouter/M0LTE/rhp2lib-net#7) so the request doesn't get
        // silently dropped.
        RequireFixture();

        await using var client = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeARhpPort);

        var connectedTcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.StatusChanged += (_, e) =>
        {
            if (e.Message.Flags is int f && (f & (int)StatusFlags.Connected) != 0)
                connectedTcs.TrySetResult(true);
        };

        var h = await client.SocketAsync(ProtocolFamily.Ax25, SocketMode.Stream);
        await client.BindAsync(h, local: "G9DUM", port: "1");
        await client.ConnectAsync(h, remote: _fx.NodeACallsign);
        await connectedTcs.Task.WaitAsync(ConnectTimeout);

        // Small send — CONNECTED, not BUSY.
        var smallReply = await client.SendOnHandleAsync(h, "x\r");
        Assert.NotNull(smallReply.Status);
        Assert.True(((StatusFlags)smallReply.Status!.Value & StatusFlags.Connected) != 0);

        // 4 KB send — fills the AX.25 send window, BUSY is asserted.
        var bigReply = await client.SendOnHandleAsync(h, new string('X', 4096));
        Assert.NotNull(bigReply.Status);
        var bigFlags = (StatusFlags)bigReply.Status!.Value;
        Assert.True((bigFlags & StatusFlags.Busy) != 0,
            $"expected BUSY in {bigFlags} after a 4 KB send");

        try { await client.CloseAsync(h); } catch { }
    }

    [SkippableFact]
    public async Task Listen_On_Dgram_Socket_Returns_Operation_Not_Supported()
    {
        // Pin: real xrouter responds to `listen` on a DGRAM socket with
        // errCode 16 ("Operation not supported"). The DGRAM socket
        // still receives matching UI frames whether or not listen was
        // called — see Dgram_Sendto_Delivers_UI_Frame_To_Peer_Listener
        // where the receive path works without a successful listen.
        RequireFixture();

        await using var client = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeARhpPort);
        var h = await client.SocketAsync(ProtocolFamily.Ax25, SocketMode.Dgram);
        await client.BindAsync(h, local: "G9DUM-9", port: "2");

        var ex = await Assert.ThrowsAsync<RhpServerException>(
            async () => await client.ListenAsync(h));
        Assert.Equal(RhpErrorCode.OperationNotSupported, ex.ErrorCode);

        try { await client.CloseAsync(h); } catch { }
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

    [SkippableFact]
    public async Task Passive_Listener_Receives_Accept_When_Peer_Connects()
    {
        // Node A binds a fresh callsign, listens.  Node B connects to
        // it.  Node A should fire the Accepted event with a child
        // handle and the connecting station's callsign in `remote`.
        RequireFixture();

        const string ListenerCall = "G9DUM-2";
        const string CallerCall   = "G8PZT";

        await using var nodeA = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeARhpPort);
        await using var nodeB = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeBRhpPort);

        var acceptedTcs = new TaskCompletionSource<AcceptMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        nodeA.Accepted += (_, e) => acceptedTcs.TrySetResult(e.Message);

        var listener = await nodeA.SocketAsync(ProtocolFamily.Ax25, SocketMode.Stream);
        await nodeA.BindAsync(listener, local: ListenerCall, port: "2");
        await nodeA.ListenAsync(listener);

        var caller = await nodeB.SocketAsync(ProtocolFamily.Ax25, SocketMode.Stream);
        await nodeB.BindAsync(caller, local: CallerCall, port: "2");
        await nodeB.ConnectAsync(caller, remote: ListenerCall);

        var accept = await acceptedTcs.Task.WaitAsync(ConnectTimeout);
        Assert.Equal(listener, accept.Handle);
        Assert.True(accept.Child > 0);
        Assert.Equal(CallerCall, accept.Remote);
        Assert.Equal(ListenerCall, accept.Local);
        // Real xrouter emits port as a JSON string. The library normalises
        // that to a string regardless of source-side casing.
        Assert.Equal("2", accept.Port);

        try { await nodeB.CloseAsync(caller); } catch { }
        try { await nodeA.CloseAsync(listener); } catch { }
    }

    [SkippableFact]
    public async Task Peer_Initiated_Close_Fires_Closed_Event_On_Listener_Side()
    {
        // After the peer closes, xrouter delivers a `close` notification
        // (not a closeReply) addressed to the child handle on the
        // listener side. Verify our Closed event surfaces it.
        RequireFixture();

        const string ListenerCall = "G9DUM-3";
        const string CallerCall   = "G8PZT-1";

        await using var nodeA = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeARhpPort);
        await using var nodeB = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeBRhpPort);

        var acceptedTcs = new TaskCompletionSource<AcceptMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closedTcs = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        nodeA.Accepted += (_, e) => acceptedTcs.TrySetResult(e.Message);
        nodeA.Closed += (_, e) => closedTcs.TrySetResult(e.Handle);

        var listener = await nodeA.SocketAsync(ProtocolFamily.Ax25, SocketMode.Stream);
        await nodeA.BindAsync(listener, local: ListenerCall, port: "2");
        await nodeA.ListenAsync(listener);

        var caller = await nodeB.SocketAsync(ProtocolFamily.Ax25, SocketMode.Stream);
        await nodeB.BindAsync(caller, local: CallerCall, port: "2");
        await nodeB.ConnectAsync(caller, remote: ListenerCall);

        var accept = await acceptedTcs.Task.WaitAsync(ConnectTimeout);

        // Caller closes — listener should see a Close on the child handle.
        await nodeB.CloseAsync(caller);

        var closedHandle = await closedTcs.Task.WaitAsync(DataTimeout);
        Assert.Equal(accept.Child, closedHandle);

        try { await nodeA.CloseAsync(listener); } catch { }
    }

    [SkippableFact]
    public async Task Trace_Listener_Captures_Sabm_Ua_And_Iframe_With_Decoded_Fields()
    {
        // Open a TRACE listener on the loopback port, generate AX.25
        // traffic on the same port, and verify the trace recv frames
        // surface decoded fields (frametype, srce/dest, ctrl, pid, ptcl).
        // Crucially, port arrives as a JSON number in TRACE mode — the
        // library now normalises that to string via StringOrIntConverter.
        RequireFixture();

        await using var traceClient = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeARhpPort);
        await using var trafficClient = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeARhpPort);

        var sabmSeen = new TaskCompletionSource<RecvMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var iframeSeen = new TaskCompletionSource<RecvMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        traceClient.Received += (_, e) =>
        {
            if (e.Message.FrameType == "C")     sabmSeen.TrySetResult(e.Message);
            if (e.Message.FrameType == "I" && !string.IsNullOrEmpty(e.Message.Data))
                iframeSeen.TrySetResult(e.Message);
        };

        var traceHandle = await traceClient.OpenAsync(
            ProtocolFamily.Ax25, SocketMode.Trace,
            port: "1",
            flags: OpenFlags.TraceIncoming | OpenFlags.TraceOutgoing | OpenFlags.TraceSupervisory);
        Assert.True(traceHandle > 0);

        // Drive AX.25 traffic on port 1 (loopback) by connecting to the
        // node's own callsign — that lands at the command processor.
        var caller = await trafficClient.SocketAsync(ProtocolFamily.Ax25, SocketMode.Stream);
        await trafficClient.BindAsync(caller, local: "G9DUM", port: "1");
        await trafficClient.ConnectAsync(caller, remote: _fx.NodeACallsign);
        await Task.Delay(500);
        await trafficClient.SendOnHandleAsync(caller, "i\r");

        var sabm = await sabmSeen.Task.WaitAsync(ConnectTimeout);
        Assert.Equal("1", sabm.Port);              // numeric on the wire, string in the model
        Assert.Equal("C", sabm.FrameType);
        Assert.Equal("G9DUM", sabm.Srce);
        Assert.Equal(_fx.NodeACallsign, sabm.Dest);

        var iframe = await iframeSeen.Task.WaitAsync(DataTimeout);
        Assert.Equal("I", iframe.FrameType);
        Assert.Equal("DATA", iframe.Ptcl);
        Assert.NotNull(iframe.Pid);
        Assert.NotNull(iframe.Ilen);

        try { await trafficClient.CloseAsync(caller); } catch { }
        try { await traceClient.CloseAsync(traceHandle); } catch { }
    }

    [SkippableFact]
    public async Task Dgram_Sendto_Delivers_UI_Frame_To_Peer_Listener()
    {
        // AX.25 DGRAM = UI frames. Node A binds a dgram socket and
        // listens passively (xrouter rejects `listen` on dgram with
        // "Operation not supported", but the bound socket still
        // receives matching UI frames). Node B sends a UI to the
        // bound callsign via sendto. The recv has port as a JSON
        // string, plus local/remote addressing.
        RequireFixture();

        const string AReceiverCall = "G9DUM-4";
        const string BSenderCall   = "G8PZT-3";
        const string Payload       = "hello UI\r";

        await using var nodeA = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeARhpPort);
        await using var nodeB = await RhpClient.ConnectAsync(_fx.Host, _fx.NodeBRhpPort);

        var recvTcs = new TaskCompletionSource<RecvMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        nodeA.Received += (_, e) =>
        {
            if (e.Message.Data == Payload) recvTcs.TrySetResult(e.Message);
        };

        var receiver = await nodeA.SocketAsync(ProtocolFamily.Ax25, SocketMode.Dgram);
        await nodeA.BindAsync(receiver, local: AReceiverCall, port: "2");

        var sender = await nodeB.SocketAsync(ProtocolFamily.Ax25, SocketMode.Dgram);
        await nodeB.BindAsync(sender, local: BSenderCall, port: "2");
        await nodeB.SendToAsync(sender,
            data: Payload,
            port: "2",
            local: BSenderCall,
            remote: AReceiverCall);

        var recv = await recvTcs.Task.WaitAsync(DataTimeout);
        Assert.Equal(receiver, recv.Handle);
        Assert.Equal(BSenderCall, recv.Remote);
        Assert.Equal(AReceiverCall, recv.Local);
        Assert.Equal("2", recv.Port);   // string in DGRAM mode
        Assert.Equal(Payload, recv.Data);

        try { await nodeA.CloseAsync(receiver); } catch { }
        try { await nodeB.CloseAsync(sender); } catch { }
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
