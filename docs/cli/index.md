# `rhp` CLI

`rhp` is a single binary with five sub-commands.  All of them share a
common option vocabulary so you can swap between, say, `rhp probe` and
`rhp chat` without re-learning the flags.

```text
rhp <command> [options]

COMMANDS
  chat     Open an interactive AX.25 / NetRom STREAM session.
  mon      Monitor a radio port in TRACE mode (decoded headers + payload).
  send     Transmit a one-shot UI/datagram frame and exit.
  probe    Connect to an RHP node, run AUTH (if requested), report status.
  serve    Run a local mock RHPv2 server (developer harness).
```

## Common options

| Flag                 | Meaning                                         | Default       |
|----------------------|-------------------------------------------------|---------------|
| `--host <host>`      | RHP host                                        | `127.0.0.1`   |
| `--port <port>`      | TCP port                                        | `9000`        |
| `--user <user>`      | AUTH username (sent if non-empty)               | _(none)_      |
| `--pass <pass>`      | AUTH password                                   | _(empty)_     |
| `--pfam <family>`    | `ax25` / `netrom` / `inet` / `unix`             | `ax25`        |
| `--mode <mode>`      | `stream` / `dgram` / `trace` / `raw`            | per command   |
| `--radio <port>`     | XRouter radio port id (e.g. `"1"`)              | _(required)_  |
| `--local <call>`     | Local callsign / address                        | _(per cmd)_   |
| `--remote <call>`    | Remote callsign / address                       | _(per cmd)_   |
| `-h`, `--help`       | Print command-specific help.                    |               |

## Exit codes

| Code | Meaning                              |
|------|--------------------------------------|
| 0    | Success.                             |
| 1    | Generic failure (message on stderr). |
| 64   | Usage error (missing required flag). |
| 130  | SIGINT / Ctrl-C.                     |

## Sub-command pages

- [`probe`](probe.md) — connectivity smoke test.
- [`chat`](chat.md) — interactive STREAM session.
- [`mon`](mon.md) — TRACE-mode frame monitor.
- [`send`](send.md) — one-shot UI / datagram transmitter.
- [`serve`](serve.md) — local mock RHPv2 server.
