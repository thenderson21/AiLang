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

- Node ids must be unique (`VAL001`).
- Required attributes must exist (for example `Let.name`, `Var.name`, `Lit.value`, `Call.target`).
- Module nodes require:
- `Import.path` (string) with `0` children.
- `Export.name` (identifier) with `0` children.
- Manifest node requires:
- `Project.name` (string), `Project.entryFile` (string, relative path), `Project.entryExport` (non-empty string), and `0` children.
- Child arity must match node contract (for example `Let=1`, `Var=0`, `Eq=2`, `Add=2`, `If=2..3`).
- `If` branches must be `Block` nodes where required (`VAL021`, `VAL022`).
- `Fn` must have `params` and a single `Block` body (`VAL050`).
- `Await` requires exactly one child (`Task`-typed expression).
- `Par` requires at least two children.

## Type/Capability Rules

- Validation enforces primitive compatibility for core operators (`Eq`, `Add`, `StrConcat`, etc.).
- Capability calls are permission-gated (`VAL040` family).
- Unknown call targets are rejected unless resolved as user-defined functions.

## Async Validation Rules

- `Fn(async=true)` is allowed only on function literals with a `Block` body.
- `Await` child must type-check to `Task` (or `Unknown` during partial analysis).
- `Par` branch expressions are validated independently against the same lexical snapshot.
- In compute-only async branches, effectful `sys.*` calls are rejected unless branch mode explicitly allows effects.
- Detached async work outside structured parent scopes is invalid.

## Async Diagnostics

- Async diagnostics must remain deterministic (stable code/message/nodeId).
- Recommended deterministic family:
- `VAL160`: `Await` expects exactly one child.
- `VAL161`: `Await` child must be `Task`.
- `VAL162`: `Par` requires at least two children.
- `VAL163`: effectful `sys.*` call is not allowed in compute-only async branch.
- `VAL164`: detached async work outside structured scope is forbidden.

## Contracts for `aic check`

- `aic check` uses `compiler.validate` (self-hosted) by default.
- Optional fallback is `compiler.validateHost` when explicitly requested.
- Output is canonical AOS:
- `Ok#...` when no diagnostics exist.
- first diagnostic `Err#...` when diagnostics exist.

## Change Control

- Any validation behavior change must update:
- `SPEC/VALIDATION.md`
- relevant goldens in `examples/golden/*.err`
