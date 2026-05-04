# Release notes

This page mirrors the [GitHub Releases](https://github.com/M0LTE/rhp2lib-net/releases)
feed, with a short summary per version.

## Unreleased

* Initial public docs site (mkdocs-material) wired up at
  <https://rhp2lib.pages.dev/>.
* New `RhpV2.Client.IntegrationTests` project: drives the published
  `ghcr.io/packethacking/xrouter` image via Testcontainers to pin
  client behaviour against a real RHP server. Tests skip gracefully
  when Docker isn't available. Includes a two-container fixture
  connected by AXUDP that exercises the full data path
  (`RhpClient → RHP → AX.25 L2 → AXUDP → peer node`) — real SABM/UA
  handshake, real I-frame send/recv, real orderly close.
* `connectReply` workaround: real xrouter returns a successful
  `connectReply` with `errCode = handle` (rather than 0) but
  `errText = "Ok"`. The library now treats any `connectReply` whose
  text is `"Ok"` as success regardless of the numeric code so callers
  don't see spurious `RhpServerException` throws on a working AX.25
  connect. Real failures (`"No Route"`, `"Not bound"`, etc.) still
  raise as before.
* `AcceptMessage.Port` typing: changed from `int?` to `string?` and
  routed through a `StringOrIntConverter` that accepts either a JSON
  number or a JSON string. Real xrouter sends `port` as a quoted
  string ("2") on `accept` even though PWP-0222's example shows an
  unquoted number. **Breaking** for anyone reading
  `AcceptMessage.Port` as `int?` — read as `string` and parse if you
  need an integer.
* `RecvMessage` extensions: `Port` now uses the same flexible
  `string?` converter (TRACE-mode recv frames send `port` as a JSON
  number, DGRAM-mode recv sends a string — same field, two shapes).
  New properties for fields the spec doesn't enumerate but real
  xrouter emits in TRACE mode: `Tseq`, `Ilen`, `Pid`, `Ptcl`. Added
  `Local` / `Remote` for DGRAM-mode receive addressing.
* Integration coverage broadened: passive-listener accepts an inbound
  AX.25 connection from the peer node; peer-initiated close fires
  `Closed` on the listener side; TRACE-mode listener captures real
  SABM/UA/I/RR frames with decoded fields; DGRAM `sendto` of a UI
  frame is received by the peer. All exercise real wire traffic
  through the two-container AXUDP fixture.
* Wire-format alignment: every reply now serialises `errCode` /
  `errText` with capital C/T, matching what xrouter actually emits.
  The published spec only mentions this as an AUTHREPLY quirk, but
  integration testing confirmed it applies to every reply. Reads
  remain case-insensitive so older / lowercase wire forms still parse.
* Documentation updates: protocol primer now lists the additional
  spec-vs-reality deltas observed against the real xrouter (global
  handle namespace, post-bad-auth lockout, `RHPPORT` requirement).

## 0.1.x — initial cut

* `RhpV2.Client` library targeting `net10.0`:
    * 2-byte big-endian framing.
    * Strongly-typed DTOs for all 22 RHPv2 message types.
    * `RhpClient` with async request/reply correlation and event-style
      notifications (`Received`, `Accepted`, `StatusChanged`, `Closed`,
      `Disconnected`, `UnknownReceived`).
    * Tolerates spec quirks (`errCode` vs `errcode`, `ConnectReply`
      PascalCase) on read.
* `MockRhpServer` shipped with the library.
* `rhp` CLI with `probe`, `chat`, `mon`, `send`, `serve`.
* CI matrix on ubuntu / windows / macOS.
* Release workflow that publishes self-contained single-file binaries
  for `linux-x64`, `linux-arm64`, `linux-musl-x64`, `win-x64`,
  `win-arm64`, `osx-x64`, `osx-arm64`, and packs the NuGet.
* 31-test xunit suite.
