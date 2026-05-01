# `rhp mon`

Open a TRACE-mode socket on a radio port and continuously dump every
frame the radio hears, with decoded headers.  The packet-radio
"monitor" view from classic terminals.

## Usage

```text
rhp mon --radio <port> [--pfam ax25|netrom]
        [--no-incoming] [--no-outgoing] [--no-supervisory]
        [--hex]
        [--host H] [--port P] [--user U --pass P]
```

## Flags

| Flag                  | Default | Effect                                   |
|-----------------------|---------|------------------------------------------|
| `--no-incoming`       | off     | Don't subscribe to inbound frames.       |
| `--no-outgoing`       | off     | Don't subscribe to outbound frames.      |
| `--no-supervisory`    | off     | Drop AX.25 S-frames (RR/RNR/REJ/SREJ).   |
| `--hex`               | off     | Dump payload as hex; default is printable ASCII. |

## Output format

Each frame is printed as one line plus an optional payload block:

```text
HH:MM:SS  DIR  SRC      ->DST       TYPE C/R P/F  payload
21:18:42  RX   M0XYZ    ->G8PZT     I    C   F    hello\r
21:18:43  TX   G8PZT    ->M0XYZ     RR   R   F
21:18:44  RX   M0XYZ    ->G8PZT-1   UI   C   P    NETROM beacon
```

* `DIR` is `RX`/`TX`/`??`.
* `TYPE` is the AX.25 frame type (`I`, `RR`, `UI`, `SABM`, …).
* `C/R` is the C/R bit.
* `P/F` is the poll/final bit.
* In `--hex` mode the payload is rendered as a 16-byte-wide hex table.

## Examples

=== "Watch radio port 1, all traffic"

    ```sh
    rhp mon --radio 1
    ```

=== "Hex dump of incoming frames only"

    ```sh
    rhp mon --radio 1 --no-outgoing --hex
    ```

=== "Filter to NetRom on port 2"

    ```sh
    rhp mon --pfam netrom --radio 2 --no-supervisory
    ```

## Tips

* Combine with `tee` for capture: `rhp mon --radio 1 | tee port1-$(date +%F).log`.
* `--no-supervisory` is hugely useful when a busy link is drowning in
  RR/RNR; you'll only see information frames and management frames.
* If you see nothing arriving, check that the remote side is talking on
  the right physical radio port id (the XRouter `PORT N=…` line).
