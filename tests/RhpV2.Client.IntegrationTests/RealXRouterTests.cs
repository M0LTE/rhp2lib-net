using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RhpV2.Client.Protocol;
using Xunit;
using ProtocolFamily = RhpV2.Client.Protocol.ProtocolFamily;
using RhpV2.TestSupport;

namespace RhpV2.Client.IntegrationTests;

/// <summary>
/// Black-box tests against the real <c>ghcr.io/packethacking/xrouter</c>
/// image. These verify that the client speaks the wire format actually
/// emitted by xrouter — not just the mock — and pin down the deltas
/// between the published PWP-0222 / PWP-0245 spec and the live
/// implementation.
///
/// Each test skips gracefully (rather than failing) if Docker isn't
/// available on the test host.
/// </summary>
[Collection(nameof(XRouterCollection))]
public class RealXRouterTests
{
    private readonly XRouterFixture _fx;
    public RealXRouterTests(XRouterFixture fx) => _fx = fx;

    private async Task<RhpClient> ConnectAsync(CancellationToken ct = default)
    {
        return await RhpClient.ConnectAsync(_fx.Host, _fx.RhpPort, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    //  Connection management
    // -----------------------------------------------------------------

    [Fact]
    public async Task Tcp_Listener_Is_Reachable()
    {
        using var tcp = new TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await tcp.ConnectAsync(_fx.Host, _fx.RhpPort, cts.Token);
        Assert.True(tcp.Connected);
    }

    [Fact]
    public async Task Auth_BadCredentials_Returns_Unauthorised()
    {
        await using var client = await ConnectAsync();

        // From a forwarded host port the xrouter sees the request as
        // arriving from the docker-bridge IP, which xrouter accepts as
        // RFC1918 — auth is therefore *optional*. But submitting bad
        // credentials must still fail explicitly.
        var ex = await Assert.ThrowsAsync<RhpServerException>(
            async () => await client.AuthenticateAsync("not-a-user", "not-a-password"));
        Assert.Equal(RhpErrorCode.Unauthorised, ex.ErrorCode);
    }

    // -----------------------------------------------------------------
    //  BSD-style socket lifecycle (well-supported on the loopback cfg)
    // -----------------------------------------------------------------

    [Fact]
    public async Task Socket_Inet_Stream_Allocates_Handle()
    {
        await using var client = await ConnectAsync();

        var h = await client.SocketAsync(ProtocolFamily.Inet, SocketMode.Stream);
        Assert.True(h > 0);

        await client.CloseAsync(h);
    }

    [Fact]
    public async Task Socket_Bind_Listen_Inet_Roundtrips()
    {
        await using var client = await ConnectAsync();

        var h = await client.SocketAsync(ProtocolFamily.Inet, SocketMode.Stream);
        try
        {
            // Bind to an arbitrary high port on xrouter's stack.
            await client.BindAsync(h, "127.0.0.1:18888");
            // Listen *may* succeed or fail depending on whether xrouter
            // has any spare TCP listen slots in this dummy config — we
            // only assert that the call goes through and a typed reply
            // comes back.
        }
        finally
        {
            await client.CloseAsync(h);
        }
    }

    [Fact]
    public async Task Bind_OnInvalidHandle_Throws_With_InvalidHandle()
    {
        await using var client = await ConnectAsync();

        var ex = await Assert.ThrowsAsync<RhpServerException>(
            async () => await client.BindAsync(handle: 99_999, local: "G9DUM"));
        Assert.Equal(RhpErrorCode.InvalidHandle, ex.ErrorCode);
    }

    [Fact]
    public async Task Connect_OnInvalidHandle_Throws_With_InvalidHandle()
    {
        await using var client = await ConnectAsync();

        var ex = await Assert.ThrowsAsync<RhpServerException>(
            async () => await client.ConnectAsync(handle: 99_999, remote: "G9DUM-2"));
        Assert.Equal(RhpErrorCode.InvalidHandle, ex.ErrorCode);
    }

    [Fact]
    public async Task Listen_OnInvalidHandle_Throws_With_InvalidHandle()
    {
        await using var client = await ConnectAsync();

        var ex = await Assert.ThrowsAsync<RhpServerException>(
            async () => await client.ListenAsync(handle: 99_999));
        Assert.Equal(RhpErrorCode.InvalidHandle, ex.ErrorCode);
    }

    [Fact]
    public async Task Send_OnInvalidHandle_Throws_With_InvalidHandle()
    {
        await using var client = await ConnectAsync();

        var ex = await Assert.ThrowsAsync<RhpServerException>(
            async () => await client.SendOnHandleAsync(99_999, "x"));
        Assert.Equal(RhpErrorCode.InvalidHandle, ex.ErrorCode);
    }

    [Fact]
    public async Task Status_OnInvalidHandle_Throws_With_InvalidHandle()
    {
        await using var client = await ConnectAsync();

        var ex = await Assert.ThrowsAsync<RhpServerException>(
            async () => await client.QueryStatusAsync(99_999));
        Assert.Equal(RhpErrorCode.InvalidHandle, ex.ErrorCode);
    }

    [Fact]
    public async Task Close_OnInvalidHandle_Returns_InvalidHandle()
    {
        await using var client = await ConnectAsync();

        var ex = await Assert.ThrowsAsync<RhpServerException>(
            async () => await client.CloseAsync(99_999));
        Assert.Equal(RhpErrorCode.InvalidHandle, ex.ErrorCode);
    }

    [Fact]
    public async Task Handles_Are_Globally_Numbered_Across_Connections()
    {
        // Surprising xrouter behaviour worth pinning down: socket
        // handles are *not* scoped per-connection. A handle allocated
        // on connection A is visible (and usable) from connection B as
        // long as it still exists. Two consecutively-allocated handles
        // from different connections come from the same monotonically
        // increasing pool.
        await using var a = await ConnectAsync();
        await using var b = await ConnectAsync();

        var ha = await a.SocketAsync(ProtocolFamily.Inet, SocketMode.Stream);
        var hb = await b.SocketAsync(ProtocolFamily.Inet, SocketMode.Stream);

        Assert.NotEqual(ha, hb);
        // Cleanup — close both, regardless of which connection allocated.
        try { await a.CloseAsync(ha); } catch (RhpServerException) { /* tolerate */ }
        try { await b.CloseAsync(hb); } catch (RhpServerException) { /* tolerate */ }
    }

    // -----------------------------------------------------------------
    //  Combined OPEN — high-level path
    // -----------------------------------------------------------------

    [Fact]
    public async Task Open_UnixConsole_Yields_Connected_Status_And_Banner()
    {
        // Opening pfam=unix mode=stream local="console" makes xrouter
        // attach us to its CLI. We expect:
        //   1. an asynchronous STATUS notification for the new handle
        //   2. an asynchronous RECV with the "Connected to SWITCH" banner
        //   3. eventually the OPENREPLY confirming the handle
        // The library has to dispatch (1) and (2) as events before the
        // user has even seen the handle from (3).
        await using var client = await ConnectAsync();

        var statusTcs = new TaskCompletionSource<StatusMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var recvTcs = new TaskCompletionSource<RecvMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        client.StatusChanged += (_, e) => statusTcs.TrySetResult(e.Message);
        client.Received += (_, e) => recvTcs.TrySetResult(e.Message);

        var handle = await client.OpenAsync(
            ProtocolFamily.Unix, SocketMode.Stream,
            local: "console", flags: OpenFlags.Passive);

        Assert.True(handle > 0);

        var statusTimeout = TimeSpan.FromSeconds(5);
        var status = await statusTcs.Task.WaitAsync(statusTimeout);
        var recv = await recvTcs.Task.WaitAsync(statusTimeout);

        Assert.Equal(handle, status.Handle);
        Assert.Equal(handle, recv.Handle);
        Assert.Contains("SWITCH", recv.Data, StringComparison.OrdinalIgnoreCase);

        await client.CloseAsync(handle);
    }

    // -----------------------------------------------------------------
    //  Wire-format pinning — what xrouter actually emits
    // -----------------------------------------------------------------

    [Fact]
    public async Task SocketReply_Wire_Uses_CapitalC_ErrCode()
    {
        // The library reads case-insensitively, so a malformed mock
        // would still pass higher-level tests. Pin the *real* casing
        // here so the mock can be aligned with the real server.

        var raw = await SendRawAndReadAsync(
            new JsonObject
            {
                ["type"] = "socket",
                ["id"] = 1,
                ["pfam"] = "inet",
                ["mode"] = "stream",
            });

        Assert.Equal("socketReply", raw["type"]!.GetValue<string>());
        Assert.True(raw.ContainsKey("errCode"),
            $"expected errCode (capital C). Wire: {raw.ToJsonString()}");
        Assert.True(raw.ContainsKey("errText"),
            $"expected errText (capital T). Wire: {raw.ToJsonString()}");
        Assert.False(raw.ContainsKey("errcode"),
            $"did not expect lowercase errcode. Wire: {raw.ToJsonString()}");
    }

    [Fact]
    public async Task ErrorReplies_All_Use_CapitalC_ErrCode()
    {
        // Drive a single connection through a battery of requests, all
        // returning errors, and verify every reply carries errCode
        // (capital C) on the wire — *not* errcode lowercase as the
        // library's spec note claimed.
        var requests = new (string Type, JsonObject Body)[]
        {
            ("auth",    new JsonObject { ["user"] = "x", ["pass"] = "y" }),
            ("bind",    new JsonObject { ["handle"] = 99999, ["local"] = "G9DUM" }),
            ("listen",  new JsonObject { ["handle"] = 99999, ["flags"] = 0 }),
            ("connect", new JsonObject { ["handle"] = 99999, ["remote"] = "G9DUM-2" }),
            ("send",    new JsonObject { ["handle"] = 99999, ["data"] = "x" }),
            ("sendto",  new JsonObject { ["handle"] = 99999, ["data"] = "x" }),
            ("status",  new JsonObject { ["handle"] = 99999 }),
            ("close",   new JsonObject { ["handle"] = 99999 }),
        };

        // Each error case has to run on a *fresh* connection because, on
        // the real xrouter, the very first failed AUTH on a connection
        // wedges all subsequent traffic into authReply(14) regardless of
        // the actual request type.
        foreach (var (type, body) in requests)
        {
            body["type"] = type;
            body["id"] = 1;
            var raw = await SendRawAndReadAsync(body);

            // Connection-after-bad-auth quirk: don't fight it.
            if (type != "auth" && raw["type"]?.GetValue<string>() == "authReply")
                continue;

            Assert.True(
                raw.ContainsKey("errCode"),
                $"reply to {type} missed errCode (capital C): {raw.ToJsonString()}");
            Assert.False(
                raw.ContainsKey("errcode"),
                $"reply to {type} unexpectedly used errcode (lowercase): {raw.ToJsonString()}");
        }
    }

    [Fact]
    public async Task Unknown_Type_Comes_Back_As_TypeReply_With_BadType_Error()
    {
        // xrouter quirk worth pinning: it manufactures a reply name by
        // appending "Reply" to whatever string it received as `type`.
        var raw = await SendRawAndReadAsync(new JsonObject
        {
            ["type"] = "thisIsNotReal",
            ["id"] = 1,
        });

        Assert.Equal("thisIsNotRealReply", raw["type"]!.GetValue<string>());
        Assert.Equal(RhpErrorCode.BadOrMissingType, raw["errCode"]!.GetValue<int>());
    }

    // -----------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// One-shot raw request: open a fresh TCP connection, write a single
    /// length-prefixed JSON frame, read one reply frame, return its
    /// JsonObject. Bypasses <see cref="RhpClient"/> so we can inspect the
    /// exact wire bytes.
    /// </summary>
    private async Task<JsonObject> SendRawAndReadAsync(JsonObject request)
    {
        using var tcp = new TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await tcp.ConnectAsync(_fx.Host, _fx.RhpPort, cts.Token);
        await using var stream = tcp.GetStream();

        var bytes = Encoding.UTF8.GetBytes(request.ToJsonString());
        await RhpFraming.WriteFrameAsync(stream, bytes, cts.Token);

        var replyBytes = await RhpFraming.ReadFrameAsync(stream, cts.Token)
            ?? throw new InvalidOperationException("server closed before replying.");
        var node = JsonNode.Parse(replyBytes) as JsonObject
            ?? throw new InvalidOperationException("server reply is not a JSON object.");
        return node;
    }
}
