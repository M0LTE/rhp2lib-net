# Contributing

## Local build

```sh
git clone https://github.com/M0LTE/rhp2lib-net.git
cd rhp2lib-net
dotnet build RhpV2.slnx
dotnet test  RhpV2.slnx
```

The repo is a `.slnx` (the new XML solution format) listing three
projects:

* `src/RhpV2.Client/`  — the library and `MockRhpServer`.
* `src/RhpV2.Tools/`   — the `rhp` CLI.
* `tests/RhpV2.Client.Tests/` — xunit suite.

## Running the docs locally

```sh
uv venv .venv
. .venv/bin/activate
uv pip install mkdocs mkdocs-material pymdown-extensions
mkdocs serve
```

Then browse to <http://127.0.0.1:8000>.

## CI

Three workflows live under `.github/workflows/`:

| Workflow      | Trigger                  | What it does                                              |
|---------------|--------------------------|-----------------------------------------------------------|
| `ci.yml`      | push / PR to `main`      | matrix build + test on ubuntu / windows / macOS.          |
| `release.yml` | tag `v*.*.*`             | publish self-contained `rhp` for 7 RIDs + NuGet pack.     |
| `docs.yml`    | push / PR (docs paths)   | build mkdocs and deploy to Cloudflare Pages.              |

## Cutting a release

```sh
git tag v0.2.0
git push origin v0.2.0
```

The tag triggers `release.yml`, which produces:

* `rhp-{ver}-{rid}.{tar.gz|zip}` for `linux-x64`, `linux-arm64`,
  `linux-musl-x64`, `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`.
* SHA-256 manifests next to each archive.
* `RhpV2.Client.{ver}.nupkg`.

All artifacts are attached to the auto-generated GitHub Release.

## Code style

* C# 12 / .NET 10. `Nullable enable`. `LangVersion=latest`.
* No third-party runtime deps in `RhpV2.Client`.
* Avoid superfluous comments; explain *why*, not *what*.
* Tests should not need real network access — use `MockRhpServer`.

## Reporting issues

Open a [GitHub issue](https://github.com/M0LTE/rhp2lib-net/issues) with:

* The exact `rhp` version (`rhp --help` prints it indirectly via
  `--version` in future builds; for now, the commit SHA or release tag
  is fine).
* The wire transcript if you can capture one — `tcpdump -X -i lo port
  9000` is your friend.
