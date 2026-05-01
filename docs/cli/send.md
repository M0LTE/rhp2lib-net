# `rhp send`

Fire a one-shot UI-frame or datagram and exit.  Useful for beacons,
scripted notifications, APRS-style messages, and bench tests against
the mock server.

## Usage

```text
rhp send --pfam ax25|netrom --radio <port>
         --local <call> --remote <call>
         [--mode dgram]   # default
         [--hex] "PAYLOAD"
         [-]                  # read payload from stdin
```

## Examples

=== "Plain-text UI frame"

    ```sh
    rhp send --pfam ax25 --radio 1 \
        --local G8PZT --remote BEACON \
        "G8PZT: testing 123"
    ```

=== "Hex payload"

    ```sh
    rhp send --pfam ax25 --radio 1 \
        --local G8PZT --remote G8PZT-1 \
        --hex "DEADBEEF01"
    ```

=== "Pipe payload from stdin"

    ```sh
    cat alert.txt | rhp send --pfam ax25 --radio 1 \
        --local G8PZT --remote ALL -
    ```

=== "NetRom datagram to a service number"

    ```sh
    rhp send --pfam netrom --radio 0 \
        --local G8PZT-1 --remote "GB7PZT:23" \
        "ping"
    ```

## Behaviour

* Opens a socket of the given `--mode` (default `dgram`), issues a
  `sendto` with the payload, then closes the handle.
* Exit code is `0` if the server returns `errcode=0`, non-zero otherwise.
* `--hex` accepts spaces, dashes, and colons as separators (`DEAD-BEEF`,
  `DE:AD:BE:EF` etc.).

## Limits

* The single payload is bounded by the 65 535-byte RHP frame limit.
* AX.25 maxlen is dictated by your XRouter port config (`PACLEN`).
  `rhp send` doesn't fragment for you; longer-than-paclen UI frames will
  be rejected by the radio side.
