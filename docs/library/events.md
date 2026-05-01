# Events and lifecycle

`RhpClient` surfaces the asynchronous half of the protocol — anything the
server pushes without you asking — as .NET events.

## The events

| Event              | Args type                  | Fires when                                        |
|--------------------|----------------------------|---------------------------------------------------|
| `Received`         | `RhpReceivedEventArgs`     | A `recv` frame arrives.                           |
| `Accepted`         | `RhpAcceptedEventArgs`     | An `accept` frame arrives on a listener.          |
| `StatusChanged`    | `RhpStatusEventArgs`       | A server-initiated `status` arrives.              |
| `Closed`           | `RhpClosedEventArgs`       | Server told us a downlink closed.                 |
| `UnknownReceived`  | `RhpUnknownEventArgs`      | A frame with an unrecognised `type` arrived.      |
| `Disconnected`     | `Exception?`               | The TCP read loop ended (clean EOS or error).     |

Subscribe before you start sending traffic, otherwise you can race
fast-replying servers (especially [`MockRhpServer`](../testing/mock-server.md)).

## Patterns

### Print everything received on any handle

```csharp
rhp.Received += (_, e) =>
{
    var data = RhpDataEncoding.FromWireString(e.Message.Data);
    Console.WriteLine($"<- handle={e.Message.Handle} {Encoding.UTF8.GetString(data)}");
};
```

### Wait for `CONNECTED` after an active OPEN

```csharp
var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
rhp.StatusChanged += (_, e) =>
{
    var f = (StatusFlags)(e.Message.Flags ?? 0);
    if (e.Message.Handle == handle && f.HasFlag(StatusFlags.Connected))
        connected.TrySetResult();
};
await connected.Task.WaitAsync(TimeSpan.FromSeconds(30));
```

### Spawn a child task per inbound connection on a listener

```csharp
rhp.Accepted += async (_, e) =>
{
    Console.WriteLine($"in: {e.Message.Remote} -> {e.Message.Child}");
    try
    {
        await rhp.SendOnHandleAsync(e.Message.Child, "Welcome\r");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"greet failed: {ex.Message}");
    }
};
```

### Detect remote close

```csharp
rhp.Closed += (_, e) =>
    Console.WriteLine($"remote closed handle {e.Handle}");
```

## Cancellation and shutdown

* All request methods take an optional `CancellationToken`.  Cancellation
  removes the pending request from the correlation map and flips the
  awaited task into the cancelled state, but does **not** rewind the
  bytes you've already sent.
* When the underlying `Stream` ends:
    1. The read loop exits.
    2. Pending requests fault with [`RhpTransportException`](errors.md).
    3. `Disconnected` fires (with the terminating exception, if any).

!!! warning "Concurrency on event handlers"
    Events fire on the read-loop task.  Don't block them — offload work
    to `Task.Run` or a `Channel<T>` if you need to do anything heavy
    (database call, blocking I/O, large parse, etc.).
