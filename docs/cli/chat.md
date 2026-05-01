# `rhp chat`

An interactive STREAM-mode chat client over AX.25 or NetRom — the
modern equivalent of typing `c <call>` at a packet terminal.

## Usage

```text
rhp chat --pfam ax25|netrom --radio <port>
         --local <mycall> --remote <theircall>
         [--host H] [--port P] [--user U --pass P]
```

## Behaviour

* Sends each line you type as a `send` frame on the open handle, with a
  trailing `\r` (the AX.25 keyboard convention).
* Prints inbound `recv` frames as they arrive, with `\r` translated to
  `\n` for terminal sanity.
* Prints `[status]` lines whenever the link state flips (CONNECTED,
  BUSY, link lost).
* Closes cleanly on Ctrl-D (stdin EOF) or Ctrl-C.

## Example: connect to GB7PZT over AX.25

```sh
rhp chat --pfam ax25 --radio 1 --local G8PZT --remote GB7PZT
```

```text
--> connecting to GB7PZT via ax25/1 (local=G8PZT)
<-- handle 42, type messages and press Enter (Ctrl-D to quit)

[status] handle=42 Connected
*** Connected to GB7PZT
hello
hi G8PZT, welcome
b
*** Disconnected
[status] handle=42 None
--> downlink lost, closing link.
```

## Example: NetRom call

```sh
rhp chat --pfam netrom --radio 0 --local G8PZT-1 --remote GB7PZT@G8PZT
```

NetRom addresses use `usercall@nodecall[:svcnum]` per the spec.
