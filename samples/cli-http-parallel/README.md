# cli-http-parallel

Demonstrates issuing multiple HTTP requests in parallel without blocking on `httpRequestAwait`.

Pattern used:

- Start all requests with `httpRequest(...)`.
- Advance each request with `httpRequestPoll(...)`.
- Sleep briefly between poll rounds (`sys.time.sleepMs(10)`) to avoid a tight busy-spin.
- Recurse until all requests are terminal (`done` or `error`).

Run:

```bash
./tools/airun run ./samples/cli-http-parallel
```

Notes:

- This sample intentionally avoids `httpRequestAwait` in the hot path.
- It has a bounded poll budget and prints a timeout summary if requests do not complete in time.
