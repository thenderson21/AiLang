# Task: WASM Remote Transport and Fullstack Stdio Parity

## Goal

Implement dual remote transport modes for wasm and define deterministic stdio behavior across browser and host runtimes.

## Scope

1. Remote transport modes for wasm:
- `js` mode: local JS adapter (no websocket required)
- `ws` mode: websocket adapter

2. Keep VM syscall surface minimal:
- Continue using `sys.remote.call` as the transport syscall surface.
- Do not add protocol-specific convenience syscalls.
- Browser HTTP/AJAX (`fetch`/XHR) may be used as an adapter backend, but only behind capability routing (for example `cap.http.request`) and not as direct VM network syscalls.

3. Fullstack stdio/stderr/stdin mapping:
- stdout must map to both:
  - host stdout
  - browser `console.log`
- stderr must map to both:
  - host stderr
  - browser `console.error`
- stdin must accept input from both:
  - host stdin
  - browser `AiLang.stdin.push(text)` queue

## Browser API Contract

Expose a deterministic browser adapter:
- `globalThis.AiLang.stdin.push(text)`
- `globalThis.AiLang.stdin.close()`
- `globalThis.AiLang.remote.call(cap, op, value)` (or equivalent local adapter entrypoint)

Rules:
- stdin is queue-based FIFO.
- No implicit DOM reads as stdin.
- Closed + empty queue yields deterministic EOF behavior.

## Profiles and Behavior Matrix

`wasm cli`:
- Uses `wasmtime` launcher.
- stdout/stderr on host terminal.
- stdin from host stdin.

`wasm spa`:
- Supports `js` remote transport mode.
- stdout/stderr mirrored to browser console.
- stdin from `AiLang.stdin.push(...)` queue.

`wasm fullstack`:
- Supports `ws` remote transport mode.
- stdout/stderr mirrored to host and browser console.
- stdin from host stdin and `AiLang.stdin.push(...)`.

## Browser UI Parity Approach (SVG Backend)

Use the existing vector-oriented UI syscall surface as the browser rendering contract.

1. Frame mapping:
- `beginFrame` starts a new SVG frame state.
- draw syscalls map to SVG elements (`rect`, `text`, `line`, `ellipse`, `path`, etc.).
- `endFrame` finalizes frame data.
- `present` commits/swaps the rendered frame in the DOM.

2. Event mapping:
- Browser input events are translated into deterministic UI event payloads.
- Events flow back through the existing UI/event path (host queue / remote channel), not ad-hoc DOM callbacks in app logic.

3. Identity and targeting:
- Event routing should remain deterministic.
- If per-element targeting is needed, use stable IDs/tags from AiLang-side composition metadata (library/runtime convention) rather than introducing protocol-specific syscalls.

## File I/O Proposal

Keep file I/O host-minimal, capability-scoped, and deterministic.

1. Syscall surface (no protocol convenience expansion):
- `sys.fs.file.read(path) -> bytes`
- `sys.fs.file.write(path, bytes) -> void`
- `sys.fs.file.exists(path) -> bool`
- `sys.fs.dir.list(path) -> node`
- `sys.fs.path.stat(path) -> node`
- `sys.fs.path.exists(path) -> bool`
- `sys.fs.file.delete(path) -> bool`
- `sys.fs.dir.delete(path, recursive) -> bool`
- `sys.fs.dir.create(path) -> void`

2. Capability policy (deny by default):
- `cap.fs.read`
- `cap.fs.write`
- `cap.fs.delete`
- `cap.fs.list`

3. Path sandboxing:
- Normalize and validate paths before access.
- Enforce configured root allowlist (project root, optional user-data root, optional temp root).
- Reject traversal/out-of-root access deterministically.

4. Profile behavior:
- `wasm cli`: native host FS under capability policy.
- `wasm spa`: no direct browser local FS; use remote capability path or explicit virtual/in-memory adapter.
- `wasm fullstack`: host-side FS available under capability policy.

## HTTP/AJAX Mapping Policy

Use browser AJAX/fetch only as a host implementation detail behind `sys.remote.call`.

- Allowed:
  - `spa` adapter maps remote capability requests (for example `cap.http.request`) to `fetch`/XHR.
  - `fullstack` adapter maps the same capability shape over websocket/server handler.
- Not allowed:
  - introducing direct `sys.net.*` browser convenience behavior that bypasses capability routing.

Rationale:
- keeps syscall surface minimal
- preserves profile parity (`spa` and `fullstack` share one call contract)
- keeps policy/security checks centralized at capability boundary

5. Deterministic diagnostics:
- Stable codes/messages for capability denied, out-of-root path, missing file, invalid args.

6. Acceptance checks:
- Happy path read/write/exists/list/stat.
- Capability denied cases.
- Path traversal attempts (`../`) rejected.
- Cross-profile checks for spa/fullstack/cli behavior.

## Determinism and Safety Constraints

- Capability checks are identical across `js` and `ws` modes.
- Identical request/response contract for both modes.
- Unsupported capability/mode must emit deterministic diagnostics (`RUN101`/validation warning where applicable).
- Keep host mechanical; no protocol logic in VM.

## Acceptance Criteria

1. `publish --target wasm32 --wasm-profile spa` works with `js` remote mode without websocket.
2. `publish --target wasm32 --wasm-profile fullstack` works with `ws` remote mode.
3. In fullstack mode, stdout/stderr mirror to host and browser console.
4. In fullstack mode, stdin works from host stdin and `AiLang.stdin.push(...)`.
5. Tests added for:
- js mode call path
- ws mode call path
- stdout/stderr mirroring
- stdin queue FIFO and EOF semantics
6. Docs updated with wasm mode matrix and stdio behavior.

## Current Implementation Status

- Implemented:
  - `js` mode runtime call path with executable checks (adapter-present and adapter-missing deterministic failure).
  - `ws` mode runtime call path with executable checks:
    - successful HELLO/WELCOME + CALL/RESULT flow
    - handshake DENY handling
    - CALL ERROR frame handling
    - socket error handling
    - unexpected frame-type handling (handshake and call phases)
    - pending-call rejection on socket close (no hung promises)
    - pending-call rejection on socket error (no hung promises)
    - unknown response-id frames ignored without corrupting active call resolution
    - handshake-close before readiness now rejects deterministically (no hung ensureSocket)
    - reconnect after socket error is verified (first call fails, next call re-establishes and succeeds)
    - reconnect after handshake deny is verified (first call denied, next call re-establishes and succeeds)
    - bad handshake frame IDs reject deterministically and recover on reconnect
    - bad handshake frame types reject deterministically and recover on reconnect
    - invalid/non-binary websocket payloads reject deterministically and recover on reconnect
    - short/truncated websocket frames reject deterministically and recover on reconnect
    - default endpoint fallback (`ws://${location.hostname}:8765`)
  - Deterministic invalid `AIVM_REMOTE_MODE` runtime diagnostics (`RUN101`) with executable checks.
  - Browser-side stdin queue (`AiLang.stdin.push/close`) FIFO+EOF behavior with executable checks.
  - Browser output mirroring checks validating stdout/stderr land in browser output/console paths.
  - Publish-time deterministic `WASM001` warnings for unsupported `sys.process.*`, `sys.fs.*`, `sys.net.*`, and `sys.ui.*` in wasm profiles where blocked.

- Remaining:
  - Fullstack dual-source stdin parity in live runtime (host stdin + `AiLang.stdin.push(...)` active together).
  - SVG backend implementation for `sys.ui.*` browser rendering/event parity (currently explicitly warned/blocked for wasm profiles).

## Out of Scope

- New high-level protocol syscalls.
- Non-deterministic host event sources.
- UI syscall expansion (`sys.ui.*`) in this task.
