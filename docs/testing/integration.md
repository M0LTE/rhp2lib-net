# Writing integration tests

This page is a recipe book for testing code that uses `RhpV2.Client`.
The library's own xunit suite follows these patterns; yours can too.

The library has two test projects:

* **`tests/RhpV2.Client.Tests`** — fast unit tests against
  `MockRhpServer`, plus framing/codec/parser tests. Runs everywhere.
* **`tests/RhpV2.Client.IntegrationTests`** — black-box tests that
  spin up `ghcr.io/packethacking/xrouter` via Testcontainers and drive
  the real RHP server. Each test skips gracefully when Docker isn't
  available, so the project still builds (and the suite still passes)
  without a Docker daemon.

## Skeleton

```csharp
using RhpV2.Client;
using RhpV2.Client.Protocol;
using RhpV2.Client.Testing;
using Xunit;

public class MyAppTests
{
    [Fact]
    public async Task Sends_Beacon_On_Startup()
    {
        await using var server = new MockRhpServer();
        server.Start();

        await using var client = await RhpClient.ConnectAsync(
            "127.0.0.1", server.Endpoint.Port);

        // ... drive your code under test ...
        await MyApp.SendBeaconAsync(client);

        // Assert on what the client sent.
        var sent = server.ReceivedFrames.OfType<SendToMessage>().Single();
        Assert.Equal("BEACON", sent.Remote);
    }
}
```

## Awaiting an asynchronous notification

A common pattern: the mock pushes a frame, you wait for the matching
event to fire, with a timeout so a regression can't hang the suite.

```csharp
var tcs = new TaskCompletionSource<RecvMessage>(
    TaskCreationOptions.RunContinuationsAsynchronously);
client.Received += (_, e) => tcs.TrySetResult(e.Message);

await server.BroadcastAsync(new RecvMessage
{
    Handle = handle,
    Data   = "ping\r",
});

var got = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
Assert.Equal("ping\r", got.Data);
```

## Forcing a server-side error

```csharp
server.Handler = msg => msg switch
{
    SendMessage s => new SendReplyMessage
    {
        Handle  = s.Handle,
        ErrCode = RhpErrorCode.NoBuffers,
        ErrText = "No buffers",
    },
    _ => null,
};

var ex = await Assert.ThrowsAsync<RhpServerException>(
    () => client.SendOnHandleAsync(handle, "..."));
Assert.Equal(RhpErrorCode.NoBuffers, ex.ErrorCode);
```

## Testing transport teardown

```csharp
var server = new MockRhpServer { SuppressReplies = true };
server.Start();
await using var client = await RhpClient.ConnectAsync(
    "127.0.0.1", server.Endpoint.Port);

var task = client.OpenAsync(
    ProtocolFamily.Ax25, SocketMode.Stream,
    port: "1", local: "G8PZT", flags: OpenFlags.Passive);

await Task.Delay(50);          // let the request reach the mock
await server.DisposeAsync();   // tear it down

await Assert.ThrowsAnyAsync<RhpProtocolException>(async () => await task);
```

## Cookbook: parallel correlation

The library's id-based correlation is fully parallel-safe.  This is the
test that proves it:

```csharp
var tasks = Enumerable.Range(0, 32)
    .Select(_ => client.OpenAsync(
        ProtocolFamily.Ax25, SocketMode.Stream,
        port: "1", local: "G8PZT", flags: OpenFlags.Passive))
    .ToArray();

var handles = await Task.WhenAll(tasks);
Assert.Equal(handles.Length, handles.Distinct().Count());
```

## Tips

* Always wrap `MockRhpServer` and `RhpClient` in `await using`.  The
  reader loop is a `Task` — leaking it across tests will eventually
  exhaust ports.
* Subscribe to events **before** opening sockets.  Mock replies arrive
  fast enough to race the subscription.
* Wrap awaits with `WaitAsync(timeout)` so an event that never fires
  fails the test instead of hanging it.
