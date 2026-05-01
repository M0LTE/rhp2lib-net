# `rhp serve`

Run the in-process [`MockRhpServer`](../testing/mock-server.md) as a
standalone process — a developer harness that responds to `OPEN`,
`SEND`, `CLOSE` and friends with sensible defaults.

Useful for:

* CI smoke tests that don't have access to a real XRouter.
* Demo videos and screenshots.
* Bench-testing higher-level applications without keying up a radio.

## Usage

```text
rhp serve [--port 9000] [--user U --pass P]
```

* No `--user` ⇒ no authentication required (useful for the absolute
  fastest "try it" loop).
* `--user U --pass P` ⇒ AUTH is required, with that exact pair.

## Behaviour

* Binds to **127.0.0.1** only.  This is intentional — the mock isn't a
  real RHP server and shouldn't be exposed.
* Allocates fresh handles starting from `100`, increments per `open` /
  `socket`.
* Always returns `errcode=0` for valid handles, `errcode=3 (Invalid
  handle)` otherwise.
* Records every received frame in an internal queue (see the
  `ReceivedFrames` property when used as a library) so tests can assert
  against the wire format.

!!! warning "Not a real RHP server"

    The mock is not an XRouter replacement.  It does not perform any
    actual radio I/O; it does not implement NetRom routing; it does not
    enforce per-port flow control.  For end-to-end testing of your
    application's protocol behaviour against the spec, use it.  For
    radio-realistic behaviour, point at a real node.

## Example: smoke-test loop

In one terminal:

```sh
rhp serve --port 19000
```

In another:

```sh
rhp probe --port 19000
rhp send --port 19000 --pfam ax25 --radio 1 --local TEST --remote DEST "hi"
```
