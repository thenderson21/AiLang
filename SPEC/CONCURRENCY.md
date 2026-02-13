# Concurrency Contract (Draft)

Status: Design companion. Core async contracts are now normative in:

- `SPEC/IL.md`
- `SPEC/EVAL.md`
- `SPEC/VALIDATION.md`
- `SPEC/BYTECODE.md`

This document remains explanatory and design-oriented for implementation strategy details.

## Purpose

Define a deterministic, AI-friendly model for parallel execution without exposing manual threads, locks, or hidden side effects.

## Design Goals

- Deterministic results and deterministic ordering.
- Structured concurrency (tree-scoped work, no orphaned tasks).
- No shared mutable state across parallel branches.
- Explicit capability boundary for host effects (`sys.*` only).
- AOS-first representation only.

## Non-Goals

- No user-visible thread primitives.
- No ambient async/await model.
- No scheduler behavior that depends on wall-clock timing.

## Proposed Semantics

### 1. `Par` Node (Compute-Only Phase)

Proposed node contract:

- Kind: `Par`
- Required attrs: none
- Child arity: `2..N`
- Child constraints: each child is an expression node

Proposed evaluation:

1. Capture current environment snapshot.
2. Evaluate each child against that snapshot in isolated branch state.
3. Collect results in child declaration order.
4. Return `Block#...` with ordered result children.

Determinism rule:

- Branch completion timing must never affect result ordering.
- Output order is always declaration order.

Safety rule:

- In phase 1, `sys.*` calls inside `Par` branches are rejected by validation.

### 2. Structured Scope

Parallel work is lexical and must join before leaving its enclosing scope.

- No detached background execution in language core.
- Parent failure deterministically cancels unfinished children.
- Child failure deterministically fails parent scope.

### 3. Effect Boundary (Future Phase)

Effectful parallelism is only allowed through explicit syscall contracts.

- Language runtime remains deterministic.
- Host may execute I/O concurrently.
- Completion delivery to VM is reordered into deterministic declaration order before merge.

## Proposed Validation Rules

When adopted, add validator rules to enforce:

- `Par` has at least two children.
- Forbidden node kinds inside phase-1 `Par` branches: effectful `Call(target=sys.*)`.
- Stable child traversal and deterministic diagnostics.

Suggested draft diagnostic IDs:

- `VAL070`: `Par` requires at least two child expressions.
- `VAL071`: `sys.*` call not permitted inside compute-only `Par`.

## Proposed Bytecode Direction

When adopted, add deterministic VM support with structured join semantics.

Potential instruction family (names tentative):

- `PAR_BEGIN`
- `PAR_FORK`
- `PAR_JOIN`

Implementation requirement:

- Execution schedule may vary internally, but observable results and errors must be deterministic and declaration-ordered.

## High-Scale Web API Mapping

Recommended architecture with this model:

1. Keep VM compute deterministic and structured.
2. Route all network/file/time/process effects through `sys.*`.
3. Let host run effectful operations concurrently, then normalize completion order before VM merge.

This allows high throughput while preserving deterministic language semantics.

## AOS Sketch (Illustrative)

```aos
Program#p1 {
  Let#l1(name=pair) {
    Par#par1 {
      Add#a1 { Lit#v1(value=20) Lit#v2(value=22) }
      StrConcat#s1 { Lit#v3(value="node:") Lit#v4(value="ok") }
    }
  }
}
```

Expected merged value shape (conceptual): first branch result, then second branch result, always in declaration order.

## Adoption Checklist

A change is complete only when all of the following are updated together:

1. `SPEC/IL.md` (new node contracts).
2. `SPEC/EVAL.md` (evaluation semantics).
3. `SPEC/VALIDATION.md` (diagnostics/rules).
4. `SPEC/BYTECODE.md` (instruction/runtime contract).
5. Golden tests under `examples/golden`.
