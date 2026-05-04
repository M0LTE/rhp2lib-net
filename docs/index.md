# rhp2lib-net

> A .NET 10 client library, in-process mock server, and command-line toolkit
> for **RHPv2** — the JSON-over-TCP "Remote Host Protocol" used by
> [XRouter][xr] to expose its multi-protocol packet-radio engine to applications.

  [xr]: https://wiki.oarc.uk/packet:white-papers:pwp-0222

<div class="grid cards" markdown>

-   :material-package-variant:{ .lg .middle } **A real client library**

    ---

    Strongly-typed messages, async request/reply correlation, asynchronous
    notifications surfaced as .NET events. Targets `net10.0`.

    [:octicons-arrow-right-24: Library overview](library/overview.md)

-   :material-console:{ .lg .middle } **A useful CLI**

    ---

    `rhp probe`, `rhp chat`, `rhp mon`, `rhp send`, `rhp serve` — one binary,
    sub-commands.  Self-contained single-file builds for seven RIDs.

    [:octicons-arrow-right-24: CLI reference](cli/index.md)

-   :material-test-tube:{ .lg .middle } **A mock server for tests**

    ---

    `MockRhpServer` lets your code talk to a real TCP socket without an
    XRouter, byte-for-byte equivalent on the wire to what the live
    server emits.

    [:octicons-arrow-right-24: Mock server](testing/mock-server.md)

-   :material-file-document-outline:{ .lg .middle } **Spec-faithful, reality-tested**

    ---

    Implements [PWP-0222][pwp-0222] and [PWP-0245][pwp-0245].  The
    project's integration suite drives a real XRouter via Testcontainers;
    the [protocol primer](protocol.md) catalogues the deltas observed
    between the published spec and live wire format.

    [:octicons-arrow-right-24: Protocol primer](protocol.md)

</div>

  [pwp-0222]: https://wiki.oarc.uk/packet:white-papers:pwp-0222
  [pwp-0245]: https://wiki.oarc.uk/packet:white-papers:pwp-0245

## At a glance

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

## What is RHPv2?

RHPv2 is a **JSON-over-TCP** protocol used by Paula Dowie's
[XRouter](https://wiki.oarc.uk/packet:xrouter) packet engine.  An RHPv2
client opens a TCP connection (default port `9000`), sends framed JSON
messages such as `OPEN`, `SEND`, `CLOSE`, and receives `OPENREPLY`,
`SENDREPLY`, async `RECV` / `ACCEPT` / `STATUS` notifications.

Each frame is preceded by a **two-byte big-endian length**, capped at
`0xFFFF` bytes.  Up to that boundary the payload is a single JSON object
whose `type` field discriminates the message kind.

The protocol therefore lets a regular application speak AX.25, NetRom,
APRS, raw TCP, ICMP and more, by talking JSON to XRouter.

## Project layout

| Path | Purpose |
|------|---------|
| [`src/RhpV2.Client/`][gh-lib]                 | The library + `MockRhpServer`. |
| [`src/RhpV2.Tools/`][gh-tools]                | The `rhp` CLI. |
| [`tests/RhpV2.Client.Tests/`][gh-tests]       | xunit unit suite (mock-driven). |
| [`tests/RhpV2.Client.IntegrationTests/`][gh-itests] | xunit integration suite (Testcontainers + real XRouter). |
| [`.github/workflows/`][gh-wf]                 | CI, release, docs. |

  [gh-lib]:    https://github.com/M0LTE/rhp2lib-net/tree/main/src/RhpV2.Client
  [gh-tools]:  https://github.com/M0LTE/rhp2lib-net/tree/main/src/RhpV2.Tools
  [gh-tests]:  https://github.com/M0LTE/rhp2lib-net/tree/main/tests/RhpV2.Client.Tests
  [gh-itests]: https://github.com/M0LTE/rhp2lib-net/tree/main/tests/RhpV2.Client.IntegrationTests
  [gh-wf]:     https://github.com/M0LTE/rhp2lib-net/tree/main/.github/workflows

## Status

!!! warning "Pre-release"

    This is `0.x` software.  The wire protocol it implements is stable, but the
    .NET API surface — particularly anything in `RhpV2.Client.Testing` — may
    still shift before `1.0`.

[Get started :material-arrow-right:](getting-started.md){ .md-button .md-button--primary }
[Download :material-download:](https://github.com/M0LTE/rhp2lib-net/releases/latest){ .md-button }
[Protocol primer :material-arrow-right:](protocol.md){ .md-button }
