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
- Child arity must match node contract (for example `Let=1`, `Var=0`, `Eq=2`, `Add=2`, `If=2..3`, `Loop=1`, `Break=0`, `Continue=0`).
- `If` branches must be `Block` nodes where required (`VAL021`, `VAL022`).
- `Fn` must have `params` and a single `Block` body (`VAL050`).
- `Await` must have exactly one child (`VAL167`).
- `Par` must have at least two child expressions (`VAL168`).

## Type/Capability Rules

- Validation enforces primitive compatibility for core operators (`Eq`, `Add`, `StrConcat`, etc.).
- Capability calls are permission-gated (`VAL040` family).
- Unknown call targets are rejected unless resolved as user-defined functions.

## UI Event Payload Rules

- `sys.ui.pollEvent` contract is canonical and deterministic:
- return value is a `UiEvent` node as defined by `SPEC/IL.md`.
- `type` must be one of `none`, `closed`, `click`, `key`.
- `modifiers` tokens must be unique, sorted (`StringComparer.Ordinal`), and drawn from `alt,ctrl,meta,shift`.
- For non-pointer events, `x` and `y` must be `-1`.
- For non-key events, `key` and `text` must be empty string and `repeat` must be `false`.
- `targetId` must be empty string when no deterministic destination exists.
- These constraints are semantic runtime contract requirements and must not be delegated to host-specific UI behavior.
- Host adapters may normalize transport payloads (key token naming, printable text extraction), but semantic editing behavior remains library-owned.

- `sys.ui.getWindowSize` contract is canonical and deterministic:
- return value is a `UiWindowSize` node as defined by `SPEC/IL.md`.
- `width` and `height` must be integers; `-1` is reserved for unavailable/invalid handles.

- `sys.ui.waitFrame` contract is deterministic:
- arguments are `(int windowHandle)`.
- return value is `void`; host may block until next frame/tick opportunity.
- UI libraries should prefer `sys.ui.waitFrame` to `sys.time.sleepMs` for frame pacing when host support exists.

- `sys.ui.drawImage` contract is deterministic:
- arguments are `(int windowHandle, int x, int y, int width, int height, string rgbaBase64)`.
- payload is raw RGBA8 bytes encoded as base64; semantic interpretation stays in libraries.

- `sys.str.substring(text,start,length)` and `sys.str.remove(text,start,length)` are deterministic UTF-8 text-edit helpers:
- indexing is by Unicode scalar sequence (not bytes).
- `start` is clamped to valid range, `length <= 0` is a no-op (`""` for substring, original string for remove).
- out-of-range inputs must not throw.
- `sys.str.find(text,pattern,start)` is a deterministic UTF-8 text-search helper:
- indexing is by Unicode scalar sequence (not bytes).
- `start` is clamped to valid range, empty `pattern` returns the clamped start index, and a miss returns `-1`.

- `sys.bytes.length(data)` contract:
- args are `(bytes)` and returns int length.
- `sys.bytes.at(data,index)` contract:
- args are `(bytes, int)` and returns int (`0..255`, or `-1` when out of range).
- `sys.bytes.slice(data,start,length)` contract:
- args are `(bytes, int, int)` and returns bytes.
- `sys.bytes.concat(left,right)` contract:
- args are `(bytes, bytes)` and returns bytes.
- `sys.bytes.fromBase64(text)` contract:
- args are `(string)` and returns bytes.
- `sys.bytes.toBase64(data)` contract:
- args are `(bytes)` and returns string.

- `sys.worker.start(taskName,payload)` contract:
- args are `(string, string)` and return int worker handle.
- `sys.worker.poll(workerHandle)` contract:
- args are `(int)` and return status int (`0,1,-1,-2,-3`).
- `sys.worker.result(workerHandle)` and `sys.worker.error(workerHandle)` return strings.
- `sys.worker.cancel(workerHandle)` returns bool.

- `sys.debug.emit(channel,payload)` contract:
- args are `(string, string)`.
- `sys.debug.mode()` contract:
- no args; returns string mode.
- `sys.debug.captureFrameBegin(frameId,width,height)` contract:
- args are `(int, int, int)`.
- `sys.debug.captureFrameEnd(frameId)` contract:
- args are `(int)`.
- `sys.debug.captureDraw(op,args)` contract:
- args are `(string, string)`.
- `sys.debug.captureInput(eventPayload)` contract:
- args are `(string)`.
- `sys.debug.captureState(key,valuePayload)` contract:
- args are `(string, string)`.
- `sys.debug.replayLoad(path)` contract:
- args are `(string)` and returns int replay handle.
- `sys.debug.replayNext(handle)` contract:
- args are `(int)` and returns string.
- `sys.debug.assert(cond,code,message)` contract:
- args are `(bool, string, string)`.
- `sys.debug.artifactWrite(path,text)` contract:
- args are `(string, string)` and returns bool.
- `sys.debug.traceAsync(opId,phase,detail)` contract:
- args are `(int, string, string)`.
- `sys.debug.taskReclaimStats()` contract:
- no args; returns node task-reclaim telemetry payload.
- `sys.host.openDefault(target)` contract:
- args are `(string)` and returns `bool`.
- current native host path accepts `http://` and `https://` only and must return quickly without blocking on external browser/app completion.

## Async Safety Rules

- `Fn(async=...)` is optional; when present it must be bool (`VAL166`).
- `Await` child must resolve to async task node (modeled as node-typed value in validator) (`VAL167`).
- `Par` branch validation runs in compute-only mode by default.
- `sys.*` calls are rejected in compute-only `Par` branches (`VAL169`).
- Blocking calls are rejected in lifecycle `update` context (`VAL340`).
- Current blocking target set includes:
- `sys.net.async.await`
- `sys.net.accept`
- `sys.net.tcp.accept`
- `sys.net.tcp.connect`
- `sys.net.tcp.connectTls`
- `sys.net.tcp.read`
- `sys.net.tcp.write`
- `sys.net.udp.recv`
- `sys.time.sleepMs`
- `sys.fs.file.read`
- `sys.fs.dir.list`
- `sys.fs.path.stat`
- `sys.console.readLine`
- `sys.console.readAllStdin`
- `io.readLine`
- `io.readAllStdin`
- `io.readFile`
- `httpRequestAwait`
- Async diagnostics remain deterministic (stable code/message/nodeId).

## Host Async Boundary Rules

- Validation treats `*Start` + `sys.net.async.*` patterns as the canonical non-blocking effect model for UI/update paths.
- Blocking waits in update paths remain invalid (`VAL340` set above), including `httpRequestAwait`.
- Runtime contract requirements (normative, host-enforced):
- `*Start` syscalls must not block on operation completion.
- `sys.net.async.poll`/`sys.net.async.result*`/`sys.net.async.error` must be non-blocking.
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
