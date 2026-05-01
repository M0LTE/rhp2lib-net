# Release notes

This page mirrors the [GitHub Releases](https://github.com/M0LTE/rhp2lib-net/releases)
feed, with a short summary per version.

## Unreleased

* Initial public docs site (mkdocs-material) wired up at
  <https://rhp2lib.pages.dev/>.

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
