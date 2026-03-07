# Evaluation Contract

This file is normative for `aic run` evaluation behavior.

## Determinism

- Evaluation is pure except explicit native calls.
- No randomness, clock, network, or hidden side effects.
- Child/statement order is always left-to-right.

## Environment Model

- Environment is passed explicitly through evaluation state.
- `Let` returns a new environment with one additional binding.
- `Var` lookup scans bindings deterministically.
- Missing variable returns runtime `Err`.

## Node Semantics

- `Program`: evaluate each child; result is last child value (or `void` if empty).
- `Block`: same as `Program`.
- `Let(name)`: evaluate child expression; bind to `name`; expression result is `void`.
- `Var(name)`: return bound value.
- `Lit`: return literal value.
- `If`: evaluate condition; must be bool literal; evaluate selected branch only.
- `Loop`: repeatedly evaluate body until exited with `Break`.
- `Break`: valid only inside `Loop`; exits nearest loop.
- `Continue`: valid only inside `Loop`; skips to next nearest-loop iteration.
- `Eq`: evaluate both sides, then compare by primitive type and value.
- `StrConcat`: evaluate both sides, convert to string form, concatenate.
- `Add`: evaluate both sides, both must be int literals.
- `bytes` values are first-class runtime values and participate in equality by length+byte-content.
- `Call`: evaluate arguments, then dispatch:
- native target (`io.*`, `compiler.*`) dispatches directly.
- otherwise resolve function binding, apply closure with captured env.
- async function call (`Fn(async=true)`) returns `Task(handle=...)` and schedules child evaluation under structured parent scope.
- `Await`: evaluate child expression; child must resolve to `Task`; block until completion and return resolved value or propagate resolved `Err`.
- `Par`: evaluate each child in isolated branch state from the same lexical snapshot, then join all branches before continuing.
- `Import(path)`: resolve path relative to current module file; parse via frontend; validate with `validate.aos`; evaluate imported module in isolated environment; merge only names explicitly listed by `Export`.
- `Export(name)`: mark an existing binding as exported from the current module evaluation scope.

## Module Rules

- Import resolution is strictly relative (absolute paths are rejected).
- Circular imports fail deterministically with runtime `Err`.
- Missing import files fail deterministically with runtime `Err`.

## UI Syscall Surface (Minimal Contract)

- VM-level UI syscalls are intentionally minimal and composable primitives.
- Current required VM UI syscall set is:
- `sys.ui.createWindow`
- `sys.ui.beginFrame`
- `sys.ui.drawRect`
- `sys.ui.drawText`
- `sys.ui.drawLine`
- `sys.ui.drawEllipse`
- `sys.ui.drawPath`
- `sys.ui.drawPolyline`
- `sys.ui.drawPolygon`
- `sys.ui.endFrame`
- `sys.ui.pollEvent`
- `sys.ui.waitFrame`
- `sys.ui.present`
- `sys.ui.closeWindow`
- `sys.ui.getWindowSize`
- High-level style/composition effects (for example text-on-path layout, paint bundles, blur/shadow filters, and group transform helpers) are not VM syscall contracts and must be composed above VM (for example in AiVectra) from minimal primitives.

## Async Determinism Rules

- Observable result ordering is declaration order, never completion order.
- Parent scope completion requires all child async work to resolve or fail.
- On first branch failure in a structured async scope, unresolved sibling branches are deterministically canceled.
- Cancellation and failure propagation always resolve to deterministic `Err` values with stable `code/message/nodeId`.
- Async execution may overlap in host scheduling, but language-visible state transitions must remain deterministic.
- Effectful operations should be modeled as non-blocking start/poll/result state transitions.
- Host may complete effectful work asynchronously, but language-visible state mutations occur only inside the deterministic evaluator/event loop step.

## Host Threading Contract (UI Owner + Workers)

- AiLang/AiVM language-visible execution is single-owner and deterministic.
- Exactly one owner thread (the evaluator/UI loop thread) may apply VM state transitions.
- Host/runtime may use internal worker threads for effectful operations (network, file, process, heavy compute), but workers must not mutate VM state directly.
- Worker completion is observed only through explicit syscall polling/result reads in evaluator steps.
- UI rendering and input consumption are owner-thread responsibilities:
- event step (`sys.ui.pollEvent`)
- deterministic state transition/recompute
- present (`sys.ui.present`)
- pacing (`sys.ui.waitFrame` when available)
- Worker scheduling order/timing may vary; observable program behavior for identical input/event logs must remain deterministic.

## Non-Blocking Async Syscall Contract

- Effectful async syscalls follow explicit `start -> poll -> result/cancel` phases.
- `*Start` syscalls must return quickly with an operation handle and must not wait for completion.
- `sys.net.async.poll(handle)` must be non-blocking and returns:
- `0` pending
- `1` completed-success
- `-1` completed-failure
- `-2` canceled
- `-3` unknown-handle
- `sys.net.async.resultInt(handle)`, `sys.net.async.resultBytes(handle)` (bytes payload), and `sys.net.async.error(handle)` are non-blocking reads of terminal payload state.
- `sys.net.async.cancel(handle)` is best-effort and deterministic:
- returns `false` for unknown/non-pending handles
- returns `true` only when cancellation transitions a pending op to canceled
- `sys.host.openDefault(target)` is a host launch action and must return promptly with success/failure without blocking evaluator progress on external app/browser lifetime.
- Library-level APIs (for example HTTP helpers) must not hide blocking waits in UI/event-loop hot paths; prefer poll-driven state machines.

## Worker Execution Contract (Phase 1)

- Worker APIs are host-executed effectful operations with owner-thread-visible completion:
- `sys.worker.start(taskName, payload) -> workerHandle`
- `sys.worker.poll(workerHandle) -> status`
- `sys.worker.result(workerHandle) -> string`
- `sys.worker.error(workerHandle) -> string`
- `sys.worker.cancel(workerHandle) -> bool`
- Worker task execution may overlap on host threads.
- Completion becomes language-visible only when owner thread performs polling in evaluator steps.
- For deterministic tie-breaking in app-level aggregation, ready workers must be consumed in ascending worker-handle order.
- Worker APIs do not introduce user-visible thread objects, shared-memory mutation, or lock primitives.

## Process Execution Contract (Phase 1)

- Process APIs are host-executed effectful operations with owner-thread-visible completion:
- `sys.process.spawn(command, argsNode, cwd, envNode) -> processHandle`
- `sys.process_poll(processHandle) -> status`
- `sys.process_wait(processHandle) -> status`
- `sys.process.stdout.read(processHandle) -> bytes`
- `sys.process.stderr.read(processHandle) -> bytes`
- `sys.process_kill(processHandle) -> bool`
- Status contract is deterministic (`0,1,-1,-2,-3` as defined in `SPEC/IL.md`).
- Native baseline may complete work synchronously during `sys.process.spawn`; libraries should still consume state through `poll/wait/result` calls.
- Host may implement internal scheduling/threads for process execution, but VM-visible state remains owner-thread deterministic.

## Debug Instrumentation Contract

- Debug syscalls are explicit effect syscalls, not implicit runtime side effects.
- Canonical debug syscall surface:
- `sys.debug.emit(channel, payload)`
- `sys.debug.mode()`
- `sys.debug.captureFrameBegin(frameId, width, height)`
- `sys.debug.captureFrameEnd(frameId)`
- `sys.debug.captureDraw(op, args)`
- `sys.debug.captureInput(eventPayload)`
- `sys.debug.captureState(key, valuePayload)`
- `sys.debug.replayLoad(path)`
- `sys.debug.replayNext(handle)`
- `sys.debug.assert(cond, code, message)`
- `sys.debug.artifactWrite(path, text)`
- `sys.debug.traceAsync(opId, phase, detail)`
- `sys.debug.taskReclaimStats()`
- Host may store/write debug artifacts, but debug-visible state transitions remain deterministic for identical syscall sequences.
- Replay consumption is pull-based (`debug_replayNext`) so evaluator order controls determinism.

## Update-Path Blocking Guard

- During lifecycle `update` execution, blocking call targets are runtime errors (`RUN031`).
- This includes direct and transitive calls made while `update` is active on the call stack.

## Async Non-Goals

- No detached fire-and-forget execution in language semantics.
- No implicit background retries/backoff in evaluator semantics.

## UI Targeting Semantics

- Event destination is language/runtime semantics, not host-owned behavior.
- Each input event step resolves at most one destination node id (`targetId`).
- Propagation is explicitly disabled in the base model: no capture phase, no bubble phase.
- If no interactive node is hit, `targetId` is empty string.
- If multiple candidates overlap, deterministic routing uses the first candidate in canonical draw order after applying hit-testing and clipping.

## Canonical Hit-Testing Semantics

- Hit-testing is evaluated in window coordinate space with integer coordinates.
- Candidate geometry is rectangle-based in the base model (`x`, `y`, `w`, `h`).
- A point `(px, py)` is inside a candidate iff:
- `px >= x`
- `py >= y`
- `px < x + w`
- `py < y + h`
- Nodes with non-positive width or height are non-hittable.
- Clipping is strict: pointer hits outside effective clip bounds are ignored.
- Z-order precedence is canonical draw order within the same frame:
- later drawn hittable candidate wins over earlier drawn candidate.
- Tie-breaking for same z-order is stable node-id lexical order (`StringComparer.Ordinal`).

## Focus Model

- Focus is explicit runtime state represented by a single focused node id or empty string.
- Initial focus is empty string.
- Click-to-focus behavior:
- on `click` with non-empty `targetId`, focus becomes `targetId`.
- on `click` with empty `targetId`, focus becomes empty string.
- `key` events are routed to the focused node id when focus is non-empty.
- If focused node is no longer present in the current frame, focus is cleared before routing the next key event.
- Focus transitions are deterministic state transitions and must not depend on host widget focus.

## Text Editing State Transitions

- Declarative text editing operates on explicit state only:
- `text` (string)
- `cursor` (int, inclusive range `0..len(text)`)
- `selectionStart` (int)
- `selectionEnd` (int)
- Invariant: `0 <= selectionStart <= selectionEnd <= len(text)`.
- Insert operation:
- replace selected range with inserted text; cursor moves to end of inserted text; selection collapses at cursor.
- Backspace operation:
- if selection non-empty, delete selected range.
- else delete one codepoint before cursor when cursor > 0.
- Delete operation:
- if selection non-empty, delete selected range.
- else delete one codepoint at cursor when cursor < len(text).
- All out-of-range cursor/selection inputs are clamped deterministically before mutation.
- Text editing semantics are language-owned and must not depend on host text widgets.

## UI Event Loop/Recompute Contract

- Evaluation progresses in single-event deterministic steps.
- One `UiEvent` is consumed per step (`type=none` means no input).
- For non-`none` events, evaluator performs one deterministic state transition and one full declarative tree recompute before presenting the frame.
- Recompute order is deterministic and matches canonical child order.
- Idle behavior (`type=none`) performs no implicit state mutation.
- For UI-driven loops, `sys.ui.waitFrame(windowHandle)` is the preferred host pacing primitive over `sys.time.sleepMs`, when host support is available.
- Host scheduling/timing may vary, but language-visible state transitions and presented outputs must be identical for identical input event sequences.

## Host Normalization vs Library Semantics

- Boundary rule:
- AiVM/host owns transport normalization only.
- AiLang libraries own input semantics.
- Host normalization responsibilities:
- normalize platform key identifiers to canonical tokens (for example `backspace`, `enter`, `left`, printable key tokens).
- provide printable `text` payload when available.
- populate canonical `type,key,text,x,y,modifiers,repeat,targetId` fields deterministically.
- Host must not implement declarative editing semantics (no hidden insert/delete/caret/focus mutation rules).
- Library responsibilities:
- interpret canonical key/text payloads (`isBackspaceKey`, `isDeleteKey`, `isArrowKey`, `keyToText`, etc.).
- define text-edit policy (insert/delete/caret movement/newline or tab policy).
- route key behavior by explicit focus/target state in language-visible state transitions.

## Raster Image Primitive

- `sys.ui.drawImage(windowHandle, x, y, width, height, rgbaBase64)` is a VM-level raster primitive.
- `rgbaBase64` encodes raw row-major RGBA8 bytes (`width * height * 4` bytes).
- Host responsibility is mechanical rendering only (no fit/crop/layout semantics).
- Image composition semantics (sizing policy, alignment, clipping policy choices) are library-owned.

## Result Emission

- `aic run` emits canonical AOS:
- `Ok#...` for successful value completion.
- `Err#...` for runtime error completion.

## Bytes Runtime Rules

- Syscall-returned bytes and string payloads are materialized into VM-owned arenas before becoming observable runtime values.
- `TO_STRING(bytes)` yields lowercase hex with `0x` prefix (`0x` for empty bytes).
- `sys.bytes.at(data,index)` returns `-1` when `index` is out of range.
- `sys.bytes.slice(data,start,length)` clamps start/length and never throws for range overflow.
- `sys.bytes.fromBase64(text)` uses strict base64 validation; invalid input is syscall error.

## VM Memory + Node GC Contract

- VM memory arenas are deterministic and bounded:
- `string_arena` hard cap failure emits `AIVM011` detail `AIVMM001: string arena capacity exceeded.`
- `bytes_arena` hard cap failure emits `AIVM011` detail `AIVMM002: bytes arena capacity exceeded.`
- Node arena hard cap failure emits `AIVM011` detail `AIVMM005: node arena capacity exceeded.`
- Node GC compaction is deterministic and may run proactively before hard-cap:
- policy interval `node_gc_interval_allocations = 64`
- pressure threshold `node_gc_pressure_threshold_nodes = 192`
- proactive compaction runs only when both interval and threshold conditions are met.
- Hard-cap node creation path must attempt compaction before emitting `AIVMM005`.
- Node compaction semantics:
- reachable node handles are preserved via deterministic handle remapping.
- unreachable nodes are reclaimed.
- all VM references to node handles (`stack`, `locals`, completed tasks, par values, process argv root) are remapped in one deterministic transition.
- Reset semantics:
- `aivm_reset_state` clears arena usage and high-water counters deterministically.
- `node_allocations_since_gc` resets to `0` after state reset and after a compaction attempt.
- `node_gc_attempts` counts every deterministic compaction attempt (proactive and hard-cap path).
- Memory telemetry counters are saturating (`size_t` max) and must never wrap.
- Native debug bundle memory telemetry is contractually present in:
- `config.toml` (policy constants)
- `state_snapshots.toml` (live counters)
- `diagnostics.toml` memory table (summary counters)
- Telemetry includes per-arena pressure counters:
- `node_gc_attempts`
- `string_arena_pressure_count`
- `bytes_arena_pressure_count`
- `node_arena_pressure_count`
