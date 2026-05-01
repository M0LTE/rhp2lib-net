# `rhp probe`

Connect to an RHPv2 node, optionally authenticate, and print round-trip
timings.  The fastest possible smoke test for a new deployment.

## Usage

```text
rhp probe [--host H] [--port P] [--user U --pass P]
```

## Examples

=== "Anonymous local probe"

    ```sh
    $ rhp probe
    --> dialling 127.0.0.1:9000 ...
    <-- TCP up in 4 ms
        (skipping AUTH; no --user supplied)
    OK
    ```

=== "Authenticated remote probe"

    ```sh
    $ rhp probe --host xrouter.example.org --user g8pzt --pass hunter2
    --> dialling xrouter.example.org:9000 ...
    <-- TCP up in 38 ms
    <-- AUTH ok in 41 ms
    OK
    ```

## When it fails

| Symptom                                                | Likely cause                                    |
|--------------------------------------------------------|-------------------------------------------------|
| `SocketException: Connection refused`                   | RHP not bound on that host/port.                |
| `RhpServerException: Unauthorised`                      | Wrong credentials or AUTH required and missing. |
| Hangs at "dialling" then `OperationCanceledException`   | Firewall / routing / wrong IP.                  |

## Use in CI

`rhp probe` exits 0 only when both the TCP connect and (optional) AUTH
succeed.  Drop it into pipelines as a precondition:

```sh
rhp probe --host "$RHP_HOST" --user "$RHP_USER" --pass "$RHP_PASS" \
    || { echo "::error::RHP node unhealthy"; exit 1; }
```
