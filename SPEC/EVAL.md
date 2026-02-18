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
- `Eq`: evaluate both sides, then compare by primitive type and value.
- `StrConcat`: evaluate both sides, convert to string form, concatenate.
- `Add`: evaluate both sides, both must be int literals.
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
- `sys.ui_createWindow`
- `sys.ui_beginFrame`
- `sys.ui_drawRect`
- `sys.ui_drawText`
- `sys.ui_drawLine`
- `sys.ui_drawEllipse`
- `sys.ui_drawPath`
- `sys.ui_drawPolyline`
- `sys.ui_drawPolygon`
- `sys.ui_endFrame`
- `sys.ui_pollEvent`
- `sys.ui_present`
- `sys.ui_closeWindow`
- `sys.ui_getWindowSize`
- High-level style/composition effects (for example text-on-path layout, paint bundles, blur/shadow filters, and group transform helpers) are not VM syscall contracts and must be composed above VM (for example in AiVectra) from minimal primitives.

## Async Determinism Rules

- Observable result ordering is declaration order, never completion order.
- Parent scope completion requires all child async work to resolve or fail.
- On first branch failure in a structured async scope, unresolved sibling branches are deterministically canceled.
- Cancellation and failure propagation always resolve to deterministic `Err` values with stable `code/message/nodeId`.
- Async execution may overlap in host scheduling, but language-visible state transitions must remain deterministic.

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

- `sys.ui_drawImage(windowHandle, x, y, width, height, rgbaBase64)` is a VM-level raster primitive.
- `rgbaBase64` encodes raw row-major RGBA8 bytes (`width * height * 4` bytes).
- Host responsibility is mechanical rendering only (no fit/crop/layout semantics).
- Image composition semantics (sizing policy, alignment, clipping policy choices) are library-owned.

## Result Emission

- `aic run` emits canonical AOS:
- `Ok#...` for successful value completion.
- `Err#...` for runtime error completion.
