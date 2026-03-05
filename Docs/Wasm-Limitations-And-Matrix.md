# WASM Limitations and Capability Matrix

## Purpose

Document current wasm runtime behavior and profile differences (`cli`, `spa`, `fullstack`) with explicit support/limitations.

This document is operational and should be kept in sync with runtime and publish behavior.

## Profiles

- `wasm cli`: runtime executed via wasmtime launcher.
- `wasm spa`: browser package with JS adapter and web wasm runtime.
- `wasm fullstack`: host app + `www/` browser package + remote bridge.

## Status Legend

- `native`: implemented directly on target host/runtime.
- `remote`: available through `sys.remote.call` capability adapter.
- `planned`: expected behavior but not yet complete in all paths.
- `blocked`: not available on this target/profile; should fail deterministically (`RUN101`).
- `n/a`: not applicable for this target/profile.

## Target Capability Matrix (Current Contract)

The matrix below is the authoritative limitations view for target/profile behavior.

| Capability family | native host targets (`osx/linux/windows`) | wasm cli | wasm spa | wasm fullstack |
|---|---|---|---|---|
| stdout (`io.print`, `io.write`, `sys.stdout.writeLine`) | native | native | native (browser sink) | native (host + browser mirror) |
| stderr | native | native | native (browser sink) | native (host + browser mirror) |
| stdin | native | native | native (`AiLang.stdin.push/close` queue) | planned (host stdin + `AiLang.stdin` queue) |
| `sys.remote.call` | native (if configured) | native (if configured) | native (`js` adapter) | native (`ws` adapter) |
| capability enforcement | native | native | native | native |
| file system (`sys.fs.*`) | native (capability/sandbox policy) | native (capability/sandbox policy) | blocked by default; use `remote` or virtual adapter | native on host path (capability/sandbox policy) |
| process lifecycle (`sys.process.spawn/wait/poll/kill/...`) | native | target-dependent (often blocked) | blocked | host-side only; browser path blocked unless remote adapter exposed |
| direct network syscalls (`sys.net.*`) | native | target-dependent | blocked direct; use `remote` capability path | host-side native; browser path should use `remote` capability path |
| HTTP/AJAX/fetch | native host net path | target-dependent | remote (`sys.remote.call` -> JS fetch/XHR adapter) | remote (`sys.remote.call` -> ws/server adapter, optionally host fetch path) |
| vector UI syscalls (`sys.ui.*`) | native host implementation-dependent | blocked unless host implements | blocked (`WASM001` publish warning, `RUN101` on execution) until SVG backend mapping lands | blocked (`WASM001` publish warning, `RUN101` on execution) until SVG backend mapping lands |
| crypto (`sys.crypto.*`) | native | target-dependent | profile/adapter-dependent (document per-capability) | profile/adapter-dependent (document per-capability) |
| time (`sys.time.*`) | native | target-dependent | profile/adapter-dependent (must be deterministic by contract) | profile/adapter-dependent (must be deterministic by contract) |
| env/cwd/platform/runtime identity | native | target-dependent | profile-defined strings only | profile-defined strings + host-derived where allowed |
| debug/artifact syscalls (`sys.debug.*`) | native/debug-gated | target-dependent | profile-dependent, should remain explicit and deterministic | profile-dependent, should remain explicit and deterministic |

## I/O Matrix

| Area | wasm cli | wasm spa | wasm fullstack |
|---|---|---|---|
| stdout | host stdout | browser console/output panel | host stdout + browser console |
| stderr | host stderr | browser console/error panel | host stderr + browser console |
| stdin | host stdin | `AiLang.stdin.push(...)` queue | host stdin + `AiLang.stdin.push(...)` queue |

## Remote Transport Matrix

| Area | wasm cli | wasm spa | wasm fullstack |
|---|---|---|---|
| `sys.remote.call` | optional, config-driven | `js` adapter mode | `ws` adapter mode |
| capability checks | required | required | required |
| call contract shape | shared | shared | shared |

## UI Matrix

Current strategy for browser UI parity is SVG backend mapping from vector-oriented UI draw operations.

| Area | wasm cli | wasm spa | wasm fullstack |
|---|---|---|---|
| browser rendering | N/A | SVG frame backend | SVG frame backend |
| event input | N/A | browser event -> deterministic queue | browser event -> deterministic queue |
| deterministic frame contract | N/A | beginFrame/draw/endFrame/present mapping | beginFrame/draw/endFrame/present mapping |

## File I/O Matrix

| Area | wasm cli | wasm spa | wasm fullstack |
|---|---|---|---|
| direct host fs syscalls | capability-gated | unavailable by default | capability-gated |
| remote/adapter fs | optional | recommended path | optional |
| path sandboxing | required | required for adapters | required |

## Network/HTTP Policy

- Browser HTTP/AJAX (`fetch`/XHR) is allowed only as adapter implementation behind capability-routed `sys.remote.call`.
- Do not expose protocol convenience syscalls for browser mode.
- Keep `syscall -> capability -> adapter` boundary explicit.

## Known Limitations (Current)

1. Browser wasm mode is capability-driven; unsupported syscalls must fail deterministically (`RUN101`).
2. `sys.ui.*` behavior must remain profile-mapped and deterministic; no implicit DOM side effects in app logic.
3. `spa` has no implicit terminal stdin; input must come from explicit queue APIs.
4. Profile differences must be documented before enabling new capabilities.
5. For browser targets, direct `sys.net.*` and direct local `sys.fs.*` are blocked by default unless explicitly surfaced through capability adapters.
6. Fullstack host+browser stdout/stderr mirroring is active; dual-source stdin behavior remains an explicit parity target.

## Required Test Coverage

- profile-by-profile stdout/stderr mapping checks
- stdin queue FIFO and EOF semantics
- shared `sys.remote.call` contract checks for `js` and `ws` modes
- unsupported syscall deterministic diagnostics in each profile
- parity fixture comparison between `spa` and `fullstack` for shared app behavior
