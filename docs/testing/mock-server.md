# Mock server

`MockRhpServer` is a real, in-process TCP server that speaks RHPv2.  It
ships in the same NuGet package as the client (under
`RhpV2.Client.Testing`) so downstream consumers can write integration
tests with no extra infrastructure.

The same code backs the [`rhp serve`](../cli/serve.md) command.

## When to reach for it

* Unit tests that want to exercise `RhpClient`'s framing and event
  pipeline against a real socket.
* Higher-level application tests that need a deterministic peer.
* Local development of a tool when you don't want to key up a radio.

## When *not* to reach for it

* Anything where realistic radio behaviour matters: link timing, paclen,
  REJ/SREJ recovery, NetRom routing, congestion.

## Construction

```csharp
await using var server = new MockRhpServer();    // ephemeral port
server.Start();                                  // begin accepting

await using var server2 = new MockRhpServer(port: 19000);
server2.Start();

Console.WriteLine($"Mock listening on {server.Endpoint}");
```

`Endpoint` is an `IPEndPoint`, so `server.Endpoint.Port` is the port
your test client should connect to.

## Default behaviour

The mock implements just enough of the spec to let `RhpClient` exercise
its full lifecycle:

| Incoming frame | Default reply                                                        |
|----------------|----------------------------------------------------------------------|
| `auth`         | `authReply` ok (or 14 if credentials don't match `Credentials`).     |
| `open`         | `openReply` with a fresh handle.                                     |
| `socket`       | `socketReply` with a fresh handle.                                   |
| `bind` / `listen` / `connect` / `close` | matching `*Reply` ok if handle is known.|
| `send`         | `sendReply` ok if handle is known.                                   |
| `sendto`       | `sendtoReply` ok if handle is known.                                 |
| `status` (request) | server-pushed `status` with `Connected` flag.                    |

Unknown handles produce `errcode=3 (Invalid handle)` replies.

## Hooks

### Require auth

```csharp
await using var server = new MockRhpServer
{
    RequireAuth = true,
    Credentials = ("g8pzt", "secret"),
};
server.Start();
```

### Custom replies

Override the default behaviour by setting a `Handler` delegate.  Return
`null` to defer to the default; return a message to override.

```csharp
server.Handler = msg => msg switch
{
    OpenMessage o when o.Pfam == "netrom" =>
        new OpenReplyMessage { ErrCode = RhpErrorCode.NoSuchPort, ErrText = "No such port" },
    _ => null,
};
```

### Push notifications to all clients

```csharp
await server.BroadcastAsync(new RecvMessage
{
    Handle = 100,
    Data = "PING\r",
});
```

`BroadcastAsync` auto-assigns `seqno` if you omit it.

### Suppress all replies (test timeouts)

```csharp
var server = new MockRhpServer { SuppressReplies = true };
```

The mock still reads incoming frames into `ReceivedFrames` but never
writes a reply — handy for testing client-side timeout / disconnect
handling.

## Asserting on what the client sent

Every received frame lands in `server.ReceivedFrames` (a
`ConcurrentQueue<RhpMessage>`).  Tests can dequeue it and inspect:

```csharp
await client.OpenAsync(...);

var frame = (OpenMessage)server.ReceivedFrames.Single();
Assert.Equal("ax25", frame.Pfam);
Assert.Equal((int)OpenFlags.Active, frame.Flags);
```

## Disposal

`MockRhpServer` is `IAsyncDisposable`.  Disposal:

* Stops the TCP listener.
* Cancels the accept loop.
* Disconnects any active sessions.

Connected `RhpClient`s see the disconnection, fault their pending
requests with `RhpTransportException`, and raise `Disconnected`.
