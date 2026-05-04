# Getting started

This page walks you from a clean machine to either (a) using the `rhp` CLI
binary or (b) consuming the `RhpV2.Client` library from your own .NET app.

## Requirements

* **.NET 8 SDK or newer** — the `RhpV2.Client` library multi-targets
  `net8.0` and `net10.0`, so any consuming app on .NET 8 / 9 / 10 can
  reference it.  Building the repo end-to-end (CLI + tests) requires
  the .NET 10 SDK.
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
[GitHub Release](https://github.com/M0LTE/rhp2lib-net/releases) — grab the
[**latest**](https://github.com/M0LTE/rhp2lib-net/releases/latest) and pick
the archive matching your platform:

| RID              | Archive                              |
|------------------|--------------------------------------|
| `linux-x64`      | `rhp2lib-net-X.Y.Z-linux-x64.tar.gz`         |
| `linux-arm64`    | `rhp2lib-net-X.Y.Z-linux-arm64.tar.gz`       |
| `linux-musl-x64` | `rhp2lib-net-X.Y.Z-linux-musl-x64.tar.gz`    |
| `win-x64`        | `rhp2lib-net-X.Y.Z-win-x64.zip`              |
| `win-arm64`      | `rhp2lib-net-X.Y.Z-win-arm64.zip`            |
| `osx-x64`        | `rhp2lib-net-X.Y.Z-osx-x64.tar.gz`           |
| `osx-arm64`      | `rhp2lib-net-X.Y.Z-osx-arm64.tar.gz`         |

Each archive contains a single self-contained `rhp` (or `rhp.exe`) — no
.NET runtime install required.  SHA-256 sums are alongside.

=== "Linux / macOS"

    ```sh
    curl -L -o rhp.tar.gz \
      https://github.com/M0LTE/rhp2lib-net/releases/latest/download/rhp2lib-net-X.Y.Z-linux-x64.tar.gz
    tar -xzf rhp.tar.gz
    sudo mv rhp /usr/local/bin/
    rhp --help
    ```

=== "Windows (PowerShell)"

    ```powershell
    Invoke-WebRequest `
      -Uri https://github.com/M0LTE/rhp2lib-net/releases/latest/download/rhp2lib-net-X.Y.Z-win-x64.zip `
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

```sh
dotnet add package RhpV2.Client
```

or in your `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="RhpV2.Client" Version="0.2.1" />
</ItemGroup>
```

The package multi-targets `net8.0` and `net10.0`; consumers on .NET 8,
9, or 10 all get a matching assembly via NuGet's TFM resolution.  The
.nupkg is also attached to every [GitHub Release](https://github.com/M0LTE/rhp2lib-net/releases)
if you need to pin via a local feed.

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
