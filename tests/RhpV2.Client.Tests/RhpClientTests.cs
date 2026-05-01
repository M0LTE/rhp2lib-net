using System.Threading;
using System.Threading.Tasks;
using RhpV2.Client.Protocol;
using RhpV2.Client.Testing;
using Xunit;

namespace RhpV2.Client.Tests;

public class RhpClientTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Connects_To_MockServer_And_Authenticates()
    {
        await using var server = new MockRhpServer { RequireAuth = true, Credentials = ("g8pzt", "pw") };
        server.Start();

        await using var client = await RhpClient.ConnectAsync("127.0.0.1", server.Endpoint.Port);
        await client.AuthenticateAsync("g8pzt", "pw");
        // No exception ⇒ pass.
    }

    [Fact]
    public async Task Authenticate_BadPassword_Throws_RhpServerException()
    {
        await using var server = new MockRhpServer { RequireAuth = true, Credentials = ("g8pzt", "right") };
        server.Start();
        await using var client = await RhpClient.ConnectAsync("127.0.0.1", server.Endpoint.Port);

        var ex = await Assert.ThrowsAsync<RhpServerException>(
            async () => await client.AuthenticateAsync("g8pzt", "wrong"));
        Assert.Equal(RhpErrorCode.Unauthorised, ex.ErrorCode);
    }

    [Fact]
    public async Task OpenAsync_Returns_Handle_From_Server()
    {
        await using var server = new MockRhpServer();
        server.Start();
        await using var client = await RhpClient.ConnectAsync("127.0.0.1", server.Endpoint.Port);

        var h = await client.OpenAsync(
            ProtocolFamily.Ax25, SocketMode.Stream,
            port: "1", local: "G8PZT", flags: OpenFlags.Passive);

        Assert.True(h > 0);
    }

    [Fact]
    public async Task SendOnHandle_Returns_OkReply()
    {
        await using var server = new MockRhpServer();
        server.Start();
        await using var client = await RhpClient.ConnectAsync("127.0.0.1", server.Endpoint.Port);

        var h = await client.OpenAsync(
            ProtocolFamily.Ax25, SocketMode.Stream,
            port: "1", local: "G8PZT", remote: "M0XYZ", flags: OpenFlags.Active);

        var reply = await client.SendOnHandleAsync(h, "hello\r");
        Assert.Equal(0, reply.ErrCode);
        Assert.Equal(h, reply.Handle);
    }

    [Fact]
    public async Task Send_OnInvalidHandle_Throws_With_InvalidHandle()
    {
        await using var server = new MockRhpServer();
        server.Start();
        await using var client = await RhpClient.ConnectAsync("127.0.0.1", server.Endpoint.Port);

        var ex = await Assert.ThrowsAsync<RhpServerException>(
            async () => await client.SendOnHandleAsync(99999, "x"));
        Assert.Equal(RhpErrorCode.InvalidHandle, ex.ErrorCode);
    }

    [Fact]
    public async Task Recv_FromServer_Fires_Received_Event()
    {
        await using var server = new MockRhpServer();
        server.Start();
        await using var client = await RhpClient.ConnectAsync("127.0.0.1", server.Endpoint.Port);

        var h = await client.OpenAsync(
            ProtocolFamily.Ax25, SocketMode.Stream,
            port: "1", local: "G8PZT", remote: "M0XYZ", flags: OpenFlags.Active);

        var tcs = new TaskCompletionSource<RecvMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.Received += (_, e) => tcs.TrySetResult(e.Message);

        await server.BroadcastAsync(new RecvMessage { Handle = h, Data = "ping\r" });

        var got = await tcs.Task.WaitAsync(DefaultTimeout);
        Assert.Equal(h, got.Handle);
        Assert.Equal("ping\r", got.Data);
    }

    [Fact]
    public async Task Accept_FromServer_Fires_Accepted_Event()
    {
        await using var server = new MockRhpServer();
        server.Start();
        await using var client = await RhpClient.ConnectAsync("127.0.0.1", server.Endpoint.Port);

        var listener = await client.OpenAsync(
            ProtocolFamily.Ax25, SocketMode.Stream,
            port: "1", local: "G8PZT", flags: OpenFlags.Passive);

        var tcs = new TaskCompletionSource<AcceptMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.Accepted += (_, e) => tcs.TrySetResult(e.Message);

        await server.BroadcastAsync(new AcceptMessage
        {
            Handle = listener,
            Child = 9999,
            Remote = "M0XYZ",
            Local = "G8PZT",
            Port = 1,
        });

        var got = await tcs.Task.WaitAsync(DefaultTimeout);
        Assert.Equal(listener, got.Handle);
        Assert.Equal(9999, got.Child);
        Assert.Equal("M0XYZ", got.Remote);
    }

    [Fact]
    public async Task Status_FromServer_Fires_StatusChanged()
    {
        await using var server = new MockRhpServer();
        server.Start();
        await using var client = await RhpClient.ConnectAsync("127.0.0.1", server.Endpoint.Port);

        var h = await client.OpenAsync(
            ProtocolFamily.Ax25, SocketMode.Stream,
            port: "1", local: "G8PZT", remote: "M0XYZ", flags: OpenFlags.Active);

        var tcs = new TaskCompletionSource<StatusMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.StatusChanged += (_, e) => tcs.TrySetResult(e.Message);

        await server.BroadcastAsync(new StatusMessage
        {
            Handle = h,
            Flags = (int)(StatusFlags.Connected),
        });

        var got = await tcs.Task.WaitAsync(DefaultTimeout);
        Assert.Equal(h, got.Handle);
        Assert.Equal((int)StatusFlags.Connected, got.Flags);
    }

    [Fact]
    public async Task Close_FromServer_Fires_Closed_Event()
    {
        await using var server = new MockRhpServer();
        server.Start();
        await using var client = await RhpClient.ConnectAsync("127.0.0.1", server.Endpoint.Port);

        var h = await client.OpenAsync(
            ProtocolFamily.Ax25, SocketMode.Stream,
            port: "1", local: "G8PZT", remote: "M0XYZ", flags: OpenFlags.Active);

        var tcs = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.Closed += (_, e) => tcs.TrySetResult(e.Handle);

        await server.BroadcastAsync(new CloseMessage { Handle = h });
        var got = await tcs.Task.WaitAsync(DefaultTimeout);
        Assert.Equal(h, got);
    }

    [Fact]
    public async Task ParallelRequests_Are_Correlated_By_Id()
    {
        await using var server = new MockRhpServer();
        server.Start();
        await using var client = await RhpClient.ConnectAsync("127.0.0.1", server.Endpoint.Port);

        // Issue many opens concurrently and verify each gets a distinct handle.
        var tasks = Enumerable.Range(0, 32)
            .Select(_ => client.OpenAsync(
                ProtocolFamily.Ax25, SocketMode.Stream,
                port: "1", local: "G8PZT", flags: OpenFlags.Passive))
            .ToArray();

        var handles = await Task.WhenAll(tasks);
        Assert.Equal(handles.Length, handles.Distinct().Count());
        Assert.All(handles, h => Assert.True(h > 0));
    }

    [Fact]
    public async Task Disconnected_Event_Fires_When_Server_Closes()
    {
        var server = new MockRhpServer();
        server.Start();
        await using var client = await RhpClient.ConnectAsync("127.0.0.1", server.Endpoint.Port);

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += (_, _) => tcs.TrySetResult(true);

        await server.DisposeAsync();
        var ok = await tcs.Task.WaitAsync(DefaultTimeout);
        Assert.True(ok);
    }

    [Fact]
    public async Task Pending_Requests_Fail_When_Connection_Drops()
    {
        var server = new MockRhpServer { SuppressReplies = true };
        server.Start();
        await using var client = await RhpClient.ConnectAsync("127.0.0.1", server.Endpoint.Port);

        var task = client.OpenAsync(
            ProtocolFamily.Ax25, SocketMode.Stream,
            port: "1", local: "G8PZT", flags: OpenFlags.Passive);

        // Give the request a moment to land on the server, then yank the carpet.
        await Task.Delay(50);
        await server.DisposeAsync();

        await Assert.ThrowsAnyAsync<RhpProtocolException>(async () => await task);
    }
}
