# Connecting and framing

## `RhpClient`

`RhpClient` is the public entry point.  It wraps a `Stream` (typically a
`TcpClient.GetStream()`), runs an async read loop, and exposes both
request-style methods and event notifications.

### Construction

=== "Connect over TCP"

    ```csharp
    await using var rhp = await RhpClient.ConnectAsync(
        host: "xrouter.local",
        port: RhpClient.DefaultPort,    // 9000
        ct: cancellationToken);
    ```

=== "Wrap a custom Stream"

    ```csharp
    // Useful for tests or non-TCP transports.
    var (a, b) = MakePipe();
    using var rhp = RhpClient.FromStream(a, ownsStream: true);
    ```

### Disposal

`RhpClient` implements `IAsyncDisposable`.  Disposal:

1. Cancels the read loop.
2. Faults any in-flight requests with `RhpTransportException`.
3. Closes the underlying socket.

`await using` is the right pattern.

## `RhpFraming`

`RhpFraming` is the standalone codec.  You only touch it directly if
you're building a custom transport (or an adapter for WebSocket); the
client uses it under the hood.

```csharp
public static class RhpFraming
{
    public const int MaxPayloadLength = 0xFFFF;

    public static Task WriteFrameAsync(
        Stream output, ReadOnlyMemory<byte> payload, CancellationToken ct = default);

    public static void WriteFrame(Stream output, ReadOnlySpan<byte> payload);

    public static Task<byte[]?> ReadFrameAsync(
        Stream input, CancellationToken ct = default);
}
```

Behaviour notes:

* `ReadFrameAsync` returns `null` at clean end-of-stream (zero bytes
  before a header).
* It throws `EndOfStreamException` for a partial header or partial body.
* It tolerates split TCP reads — internally it reads exactly the
  requested number of bytes.
* Writes are atomic from the codec's point of view: header and payload
  go in a single `WriteAsync`/`FlushAsync` pair.  `RhpClient` adds an
  outer `SemaphoreSlim` so concurrent writes from multiple tasks remain
  framed correctly.

## Wire encoding for binary payloads

The JSON `data` field must be a string, but RHP traffic is often binary
(AX.25 I-frames, raw NetRom packets, etc.).  Use
`RhpDataEncoding`:

```csharp
byte[] payload = ...;
string wire    = RhpDataEncoding.ToWireString(payload);   // Latin-1 1:1
byte[] back    = RhpDataEncoding.FromWireString(wire);
```

Latin-1 is chosen because every byte maps to a unique code unit;
`System.Text.Json`'s string escaper handles control bytes for you.
