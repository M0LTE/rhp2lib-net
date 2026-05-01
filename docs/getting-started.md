# Getting started

This page walks you from a clean machine to either (a) using the `rhp` CLI
binary or (b) consuming the `RhpV2.Client` library from your own .NET app.

## Requirements

* **.NET 10 SDK** — the project targets `net10.0`.
* **An RHPv2 endpoint** — typically an XRouter node listening on TCP `9000`.
  No XRouter? Run [`rhp serve`](cli/serve.md) to spin up an in-process
  mock with the same wire protocol.

!!! tip "Verifying you can reach an RHPv2 node"

    The fastest possible smoke test:

    ```sh
    rhp probe --host xrouter.local --port 9000
    ```

    Add `--user`/`--pass` if your node enforces AUTH.

## Option A: use the `rhp` CLI

Pre-built single-file binaries are attached to every
[GitHub Release](https://github.com/M0LTE/rhp2lib-net/releases).  Pick the
archive matching your platform:

| RID              | Archive                              |
|------------------|--------------------------------------|
| `linux-x64`      | `rhp-X.Y.Z-linux-x64.tar.gz`         |
| `linux-arm64`    | `rhp-X.Y.Z-linux-arm64.tar.gz`       |
| `linux-musl-x64` | `rhp-X.Y.Z-linux-musl-x64.tar.gz`    |
| `win-x64`        | `rhp-X.Y.Z-win-x64.zip`              |
| `win-arm64`      | `rhp-X.Y.Z-win-arm64.zip`            |
| `osx-x64`        | `rhp-X.Y.Z-osx-x64.tar.gz`           |
| `osx-arm64`      | `rhp-X.Y.Z-osx-arm64.tar.gz`         |

Each archive contains a single self-contained `rhp` (or `rhp.exe`) — no
.NET runtime install required.  SHA-256 sums are alongside.

=== "Linux / macOS"

    ```sh
    curl -L -o rhp.tar.gz \
      https://github.com/M0LTE/rhp2lib-net/releases/latest/download/rhp-X.Y.Z-linux-x64.tar.gz
    tar -xzf rhp.tar.gz
    sudo mv rhp /usr/local/bin/
    rhp --help
    ```

=== "Windows (PowerShell)"

    ```powershell
    Invoke-WebRequest `
      -Uri https://github.com/M0LTE/rhp2lib-net/releases/latest/download/rhp-X.Y.Z-win-x64.zip `
      -OutFile rhp.zip
    Expand-Archive rhp.zip -DestinationPath C:\Tools\rhp
    C:\Tools\rhp\rhp.exe --help
    ```

### Build the CLI from source

```sh
git clone https://github.com/M0LTE/rhp2lib-net.git
cd rhp2lib-net
dotnet build RhpV2.slnx -c Release
dotnet run --project src/RhpV2.Tools -- probe --host 127.0.0.1
```

## Option B: use the library

The `RhpV2.Client` package is published to GitHub Releases as a `.nupkg`.
A public NuGet feed is on the roadmap; for now grab the asset and add it
to a local feed, or reference the project source directly.

```xml
<ItemGroup>
  <PackageReference Include="RhpV2.Client" Version="0.1.0" />
</ItemGroup>
```

Hello, world:

```csharp
using RhpV2.Client;
using RhpV2.Client.Protocol;

await using var rhp = await RhpClient.ConnectAsync("xrouter.local");

var handle = await rhp.OpenAsync(
    ProtocolFamily.Ax25, SocketMode.Stream,
    port: "1", local: "G8PZT", remote: "GB7PZT",
    flags: OpenFlags.Active);

rhp.Received += (_, e) => Console.WriteLine(e.Message.Data);
await rhp.SendOnHandleAsync(handle, "hello\r");
```

## Where next?

- [Protocol primer](protocol.md) — what's actually on the wire.
- [Library overview](library/overview.md) — the .NET API.
- [CLI reference](cli/index.md) — every command in detail.
- [Mock server](testing/mock-server.md) — testing without a radio.
