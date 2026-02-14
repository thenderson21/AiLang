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

## Async Safety Rules

- `Fn(async=...)` is optional; when present it must be bool (`VAL166`).
- `Await` child must resolve to async task node (modeled as node-typed value in validator) (`VAL167`).
- `Par` branch validation runs in compute-only mode by default.
- `sys.*` calls are rejected in compute-only `Par` branches (`VAL169`).
- Async diagnostics remain deterministic (stable code/message/nodeId).

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
