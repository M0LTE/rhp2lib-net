# rhp2lib-net

A C# / .NET 10 client library, test harness, and command-line toolkit for
**RHPv2** — the JSON-over-TCP "Remote Host Protocol" used by XRouter to expose
its multi-protocol packet engine to applications.

📖 **Full documentation:** <https://rhp2lib.pages.dev/>
📦 **Downloads:** [GitHub Releases](https://github.com/M0LTE/rhp2lib-net/releases) — self-contained `rhp` binaries for Linux / Windows / macOS, plus the `RhpV2.Client` NuGet.

The protocol is described in
[PWP-0222](https://wiki.oarc.uk/packet:white-papers:pwp-0222) and
[PWP-0245](https://wiki.oarc.uk/packet:white-papers:pwp-0245) (Paula Dowie,
G8PZT et al., June 2023).

## Layout

```
src/RhpV2.Client/                    reusable client library + in-process mock server
src/RhpV2.Tools/                     `rhp` CLI (chat, mon, send, probe, serve)
tests/RhpV2.Client.Tests/            xunit suite (framing, codecs, mock-driven)
tests/RhpV2.Client.IntegrationTests/ Testcontainers suite — drives ghcr.io/packethacking/xrouter
```

## The library

```csharp
using RhpV2.Client;
using RhpV2.Client.Protocol;

await using var rhp = await RhpClient.ConnectAsync("xrouter.local");
await rhp.AuthenticateAsync("g8pzt", "secret");

rhp.Received += (_, e) =>
    Console.WriteLine($"<- {e.Message.Data}");

var handle = await rhp.OpenAsync(
    ProtocolFamily.Ax25, SocketMode.Stream,
    port: "1", local: "G8PZT", remote: "GB7PZT",
    flags: OpenFlags.Active);

await rhp.SendOnHandleAsync(handle, "hello\r");
```

Highlights:

* Length-prefixed (2-byte big-endian) framing, exact to spec.
* Strongly-typed messages (`AuthMessage`, `OpenMessage`, `RecvMessage`, …).
* Async request/reply correlation via auto-assigned `id` field.
* Asynchronous notifications surfaced as events (`Received`, `Accepted`,
  `StatusChanged`, `Closed`, `UnknownReceived`, `Disconnected`).
* Tolerates spec quirks (`errCode` on AUTHREPLY, `ConnectReply` PascalCase).
* Forward-compatible: unknown message types arrive as `UnknownMessage`.

## CLI (`rhp`)

```
rhp probe   --host xrouter.local --port 9000 --user g8pzt --pass …
rhp chat    --pfam ax25 --radio 1 --local G8PZT --remote GB7PZT
rhp mon     --pfam ax25 --radio 1                    # TRACE-mode monitor
rhp send    --pfam ax25 --radio 1 --local G8PZT --remote G8PZT-1 "Hi"
rhp serve   --port 9000                              # local mock for dev
```

All commands share a common `--host/--port/--user/--pass` vocabulary.

## Tests / harness

The `MockRhpServer` (in `RhpV2.Client.Testing`) is shipped with the library
itself so downstream applications can write integration tests without
spinning up a real XRouter:

```csharp
await using var server = new MockRhpServer();
server.Start();
await using var client =
    await RhpClient.ConnectAsync("127.0.0.1", server.Endpoint.Port);
// ... drive any sequence you like; assert against server.ReceivedFrames.
```

The same mock backs `rhp serve`, so the CLI doubles as a self-test driver.

## Build & test

```sh
dotnet build
dotnet test
```

* **Unit suite** (46 tests; runs everywhere): framing, codec,
  polymorphic JSON dispatch, correlated request/reply, server-pushed
  notifications, transport teardown.
* **Integration suite** (22 tests; requires Docker): pulls
  `ghcr.io/packethacking/xrouter` via Testcontainers and pins the
  client against the real RHP server. Includes a two-container
  AXUDP-linked fixture that exercises full AX.25 lifecycle paths —
  cross-network connect / send / receive / close, passive listener
  accepting an inbound connection, peer-initiated close,
  TRACE-mode frame capture (SABM/UA/I/RR with decoded fields),
  and DGRAM (UI frame) sendto/recv. Tests skip gracefully when
  Docker isn't reachable, so the suite is green without it.
