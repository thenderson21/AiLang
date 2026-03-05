# IL Contract

This file is normative for the executable AiLang IL subset used by `aic run`.

## Core Node Kinds

| Kind | Required attrs | Child arity | Notes |
|---|---|---:|---|
| `Program` | none | `0..N` | Evaluate children in order. |
| `Block` | none | `0..N` | Evaluate children in order. |
| `Let` | `name` (identifier) | `1` | Binds evaluated child result to `name`. |
| `Var` | `name` (identifier) | `0` | Reads from environment. |
| `Lit` | `value` (`string\|int\|bool\|bytes`) | `0` | Literal value node. |
| `Call` | `target` (identifier/dotted identifier) | `0..N` | Native or user-defined call. |
| `Import` | `path` (string, relative) | `0` | Loads another module and merges explicit exports. |
| `Export` | `name` (identifier) | `0` | Exposes one binding from current module. |
| `Project` | `name` (string), `entryFile` (string), `entryExport` (string) | `0..N` | Project manifest node for `project.aiproj`; children must be `Include`. |
| `Include` | `name` (string), `path` (string, relative), `version` (string) | `0` | Declares library include metadata for project-level dependency resolution. |
| `If` | none | `2..3` | Condition, then-branch, optional else-branch. |
| `Eq` | none | `2` | Value equality. |
| `StrConcat` | none | `2` | String concatenation. |
| `Add` | none | `2` | Integer addition. |
| `Fn` | `params` (identifier) | `1` | Function literal with captured env. |
| `Await` | none | `1` | Waits on async handle and yields resolved value or error. |
| `Par` | none | `2..N` | Structured parallel expression group; results preserve declaration order. |

## Value Shapes

- Runtime values are represented as nodes:
- `Lit(value=...)` for primitive values.
- Primitive runtime kinds are `string`, `int`, `bool`, and `bytes`.
- `Block#void` as the canonical void value.
- `Err(code=... message="..." nodeId=...)` for runtime errors.
- `Task(handle=...)` for async in-flight work handles returned by async calls.
- `UiEvent(...)` for UI input payloads returned by `sys.ui.pollEvent`.
- `UiWindowSize(width=... height=...)` for UI window size payloads returned by `sys.ui.getWindowSize`.
- Function values are closures represented as block nodes containing:
- function node + captured environment.

## UI Event Value Contract

- `sys.ui.pollEvent` returns one canonical `UiEvent` node per call.
- Canonical attributes on `UiEvent`:
- `type` (string): `none`, `closed`, `click`, `key`.
- `targetId` (string): target node id, or empty string when no target applies.
- `x` (int): window-space x coordinate for pointer events; `-1` when not applicable.
- `y` (int): window-space y coordinate for pointer events; `-1` when not applicable.
- `key` (string): canonical key identifier for key events, else empty string.
- `text` (string): UTF-8 text payload for text input on key events, else empty string.
- `modifiers` (string): comma-separated sorted set from `alt,ctrl,meta,shift`; empty string for none.
- `repeat` (bool): `true` only for host key-repeat events, else `false`.
- `UiEvent` has `0` children.
- Host/VM role is transport normalization only; key meaning (editing/navigation/submit policy) is defined in AiLang library code.

## UI Window Size Value Contract

- `sys.ui.getWindowSize` returns one canonical `UiWindowSize` node per call.
- Canonical attributes on `UiWindowSize`:
- `width` (int): current client-area width in pixels, or `-1` when unavailable.
- `height` (int): current client-area height in pixels, or `-1` when unavailable.
- `UiWindowSize` has `0` children.

## Async Function Contract

- `Fn` may include optional attribute `async` (bool, default `false`).
- Calling a function declared with `Fn(async=true)` returns `Task` immediately.
- `Await` resolves one `Task` value and returns the underlying value.
- `Par` evaluates multiple child expressions as a structured async scope and returns `Block` of results in declaration order.
- Async work is lexical and structured; detached background tasks are not part of IL.

## Async Non-Goals

- No user-level threads.
- No user-level locks/mutex primitives.
- No ambient scheduler primitives in language IL.

## Worker Syscall Value Contract

- `sys.worker.start(taskName, payload)` returns an int worker handle.
- `sys.worker.poll(workerHandle)` returns int status:
- `0` pending
- `1` completed-success
- `-1` completed-failure
- `-2` canceled
- `-3` unknown-handle
- `sys.worker.result(workerHandle)` returns string payload (empty when unavailable).
- `sys.worker.error(workerHandle)` returns string error code (`unknown_worker` for unknown handles).
- `sys.worker.cancel(workerHandle)` returns bool for cancellation transition success.

## Bytes Syscall Value Contract

- `sys.bytes.length(data)` returns byte length as int.
- `sys.bytes.at(data,index)` returns byte value (`0..255`) or `-1` when index is out of range.
- `sys.bytes.slice(data,start,length)` returns clamped bytes slice.
- `sys.bytes.concat(left,right)` returns concatenated bytes.
- `sys.bytes.fromBase64(text)` returns `bytes`.
- `sys.bytes.toBase64(data)` returns base64 text as string.

## Process Syscall Value Contract

- `sys.process.spawn(command, argsNode, cwd, envNode)` returns an int process handle (`-1` when start fails).
- `sys.process_poll(processHandle)` returns int status:
- `0` pending
- `1` completed-success
- `-1` completed-failure
- `-2` canceled
- `-3` unknown-handle
- `sys.process_wait(processHandle)` returns the same terminal status contract as `sys.process_poll`.
- `sys.process.stdout.read(processHandle)` and `sys.process.stderr.read(processHandle)` return bytes payloads (empty when unavailable).
- `sys.process_kill(processHandle)` returns bool for kill transition success.

## Debug Syscall Value Contract

- `sys.debug.emit(channel, payload)` writes one deterministic debug record and returns `void`.
- `sys.debug.mode()` returns current debug mode string: `off`, `live`, `snapshot`, `replay`, or `scene`.
- `sys.debug.captureFrameBegin(frameId, width, height)` and `sys.debug.captureFrameEnd(frameId)` return `void`.
- `sys.debug.captureDraw(op, args)` returns `void`; canonical `op` values: `rect`, `ellipse`, `path`, `text`, `line`, `transform`, `filter`, `image`.
- `sys.debug.captureInput(eventPayload)` and `sys.debug.captureState(key, valuePayload)` return `void`.
- `sys.debug.replayLoad(path)` returns int replay handle (`-1` on load failure).
- `sys.debug.replayNext(handle)` returns next replay record string, or empty string at EOF/unknown handle.
- `sys.debug.assert(cond, code, message)` returns `void` when `cond=true`, otherwise raises deterministic runtime error.
- `sys.debug.artifactWrite(path, text)` returns bool success.
- `sys.debug.traceAsync(opId, phase, detail)` returns `void`; canonical `phase` values: `start`, `poll`, `done`, `fail`, `cancel`.
- `sys.debug.taskReclaimStats()` returns `DebugTaskReclaimStats(reclaimed=..., skipPinned=..., exhausted=...)` as a node payload.

## Stability Rule

- Changes to kind set, attrs, arity, or value shape require updates to:
- `SPEC/IL.md`
- related golden tests under `examples/golden`
