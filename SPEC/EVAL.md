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

## Async Determinism Rules

- Observable result ordering is declaration order, never completion order.
- Parent scope completion requires all child async work to resolve or fail.
- On first branch failure in a structured async scope, unresolved sibling branches are deterministically canceled.
- Cancellation and failure propagation always resolve to deterministic `Err` values with stable `code/message/nodeId`.
- Async execution may overlap in host scheduling, but language-visible state transitions must remain deterministic.

## Async Non-Goals

- No detached fire-and-forget execution in language semantics.
- No implicit background retries/backoff in evaluator semantics.

## Result Emission

- `aic run` emits canonical AOS:
- `Ok#...` for successful value completion.
- `Err#...` for runtime error completion.
