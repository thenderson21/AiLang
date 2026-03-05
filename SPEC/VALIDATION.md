# Validation Contract

This file is normative for semantic validation used by `aic check` (default path: `validate.aos`).

## Guarantees

- Validation is deterministic.
- Traversal order is stable and recursive (node, then children).
- Diagnostics are emitted as `Err` nodes with:
- `code` (identifier)
- `message` (string)
- `nodeId` (identifier)

## Required Structural Rules

- Source may omit node ids (`Kind(...)` / `Kind { ... }`); parser/canonicalizer assigns deterministic ids before validation.
- Node ids in the canonical tree must be unique (`VAL001`).
- Required attributes must exist (for example `Let.name`, `Var.name`, `Lit.value`, `Call.target`).
- Module nodes require:
- `Import.path` (string) with `0` children.
- `Export.name` (identifier) with `0` children.
- Manifest node requires:
- `Project.name` (string), `Project.entryFile` (string, relative path), `Project.entryExport` (non-empty string), and `0` children.
- Child arity must match node contract (for example `Let=1`, `Var=0`, `Eq=2`, `Add=2`, `If=2..3`).
- `If` branches must be `Block` nodes where required (`VAL021`, `VAL022`).
- `Fn` must have `params` and a single `Block` body (`VAL050`).
- `Await` must have exactly one child (`VAL167`).
- `Par` must have at least two child expressions (`VAL168`).

## Type/Capability Rules

- Validation enforces primitive compatibility for core operators (`Eq`, `Add`, `StrConcat`, etc.).
- Capability calls are permission-gated (`VAL040` family).
- Unknown call targets are rejected unless resolved as user-defined functions.

## UI Event Payload Rules

- `sys.ui_pollEvent` contract is canonical and deterministic:
- return value is a `UiEvent` node as defined by `SPEC/IL.md`.
- `type` must be one of `none`, `closed`, `click`, `key`.
- `modifiers` tokens must be unique, sorted (`StringComparer.Ordinal`), and drawn from `alt,ctrl,meta,shift`.
- For non-pointer events, `x` and `y` must be `-1`.
- For non-key events, `key` and `text` must be empty string and `repeat` must be `false`.
- `targetId` must be empty string when no deterministic destination exists.
- These constraints are semantic runtime contract requirements and must not be delegated to host-specific UI behavior.
- Host adapters may normalize transport payloads (key token naming, printable text extraction), but semantic editing behavior remains library-owned.

- `sys.ui_getWindowSize` contract is canonical and deterministic:
- return value is a `UiWindowSize` node as defined by `SPEC/IL.md`.
- `width` and `height` must be integers; `-1` is reserved for unavailable/invalid handles.

- `sys.ui_waitFrame` contract is deterministic:
- arguments are `(int windowHandle)`.
- return value is `void`; host may block until next frame/tick opportunity.
- UI libraries should prefer `sys.ui_waitFrame` to `sys.time_sleepMs` for frame pacing when host support exists.

- `sys.ui_drawImage` contract is deterministic:
- arguments are `(int windowHandle, int x, int y, int width, int height, string rgbaBase64)`.
- payload is raw RGBA8 bytes encoded as base64; semantic interpretation stays in libraries.

- `sys.str_substring(text,start,length)` and `sys.str_remove(text,start,length)` are deterministic UTF-8 text-edit helpers:
- indexing is by Unicode scalar sequence (not bytes).
- `start` is clamped to valid range, `length <= 0` is a no-op (`""` for substring, original string for remove).
- out-of-range inputs must not throw.

- `sys.worker_start(taskName,payload)` contract:
- args are `(string, string)` and return int worker handle.
- `sys.worker_poll(workerHandle)` contract:
- args are `(int)` and return status int (`0,1,-1,-2,-3`).
- status mapping is deterministic: `0=pending`, `1=completed-success`, `-1=completed-failure`, `-2=canceled`, `-3=unknown-handle`.
- `sys.worker_result(workerHandle)` and `sys.worker_error(workerHandle)` return strings.
- `sys.worker_cancel(workerHandle)` returns bool.

- `sys.debug_emit(channel,payload)` contract:
- args are `(string, string)`.
- `sys.debug_mode()` contract:
- no args; returns string mode.
- `sys.debug_captureFrameBegin(frameId,width,height)` contract:
- args are `(int, int, int)`.
- `sys.debug_captureFrameEnd(frameId)` contract:
- args are `(int)`.
- `sys.debug_captureDraw(op,args)` contract:
- args are `(string, string)`.
- `sys.debug_captureInput(eventPayload)` contract:
- args are `(string)`.
- `sys.debug_captureState(key,valuePayload)` contract:
- args are `(string, string)`.
- `sys.debug_replayLoad(path)` contract:
- args are `(string)` and returns int replay handle.
- `sys.debug_replayNext(handle)` contract:
- args are `(int)` and returns string.
- `sys.debug_assert(cond,code,message)` contract:
- args are `(bool, string, string)`.
- `sys.debug_artifactWrite(path,text)` contract:
- args are `(string, string)` and returns bool.
- `sys.debug_traceAsync(opId,phase,detail)` contract:
- args are `(int, string, string)`.

## Async Safety Rules

- `Fn(async=...)` is optional; when present it must be bool (`VAL166`).
- `Await` child must resolve to async task node (modeled as node-typed value in validator) (`VAL167`).
- `Par` branch validation runs in compute-only mode by default.
- `sys.*` calls are rejected in compute-only `Par` branches (`VAL169`).
- Blocking calls are rejected in lifecycle `update` context (`VAL340`).
- Current blocking target set includes:
- `sys.net_asyncAwait`
- `sys.net_accept`
- `sys.net_readHeaders`
- `sys.net_tcpAccept`
- `sys.net_tcpConnect`
- `sys.net_tcpConnectTls`
- `sys.net_tcpRead`
- `sys.net_tcpWrite`
- `sys.net_udpRecv`
- `sys.time_sleepMs`
- `sys.fs_readFile`
- `sys.fs_readDir`
- `sys.fs_stat`
- `sys.console_readLine`
- `sys.console_readAllStdin`
- `io.readLine`
- `io.readAllStdin`
- `io.readFile`
- `httpRequestAwait`
- Async diagnostics remain deterministic (stable code/message/nodeId).

## Host Async Boundary Rules

- Validation treats `*Start` + `net_async*` patterns as the canonical non-blocking effect model for UI/update paths.
- Blocking waits in update paths remain invalid (`VAL340` set above), including `httpRequestAwait`.
- Runtime contract requirements (normative, host-enforced):
- `*Start` syscalls must not block on operation completion.
- `sys.net_asyncPoll`/`sys.net_asyncResult*`/`sys.net_asyncError` must be non-blocking.
- Host worker threads may execute effectful work, but VM state mutation is owner-thread only and must occur via deterministic evaluator steps.

## Contracts for `aic check`

- `aic check` uses `compiler.validate` (self-hosted) by default.
- Optional fallback is `compiler.validateHost` when explicitly requested.
- Output is canonical AOS:
- `Ok#...` when no diagnostics exist.
- first diagnostic `Err#...` when diagnostics exist.

## Compiler Host Calls

- `compiler.format` expects one node argument and returns canonical AOS text.
- `compiler.formatIds` expects one node argument and returns canonical AOS text with deterministic rewritten ids (`VAL171`, `VAL172` on misuse).

## Change Control

- Any validation behavior change must update:
- `SPEC/VALIDATION.md`
- relevant goldens in `examples/golden/*.err`
