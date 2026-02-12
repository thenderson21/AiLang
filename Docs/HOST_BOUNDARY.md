# Host Boundary (Normative)

This repository is AI-first. Runtime semantics belong in AOS, not C#.

## Rule

- C# host code is limited to:
  - Bootstrapping (load source/bundle, parse, validate, call runtime entry).
  - Syscall/builtin surface (`sys.*`, minimal compiler helpers needed by AOS).
  - Process plumbing (stdin/stdout, sockets, file IO, exit code).
- C# must **not** implement language/lifecycle/business semantics.
- Any lifecycle, serve/run command handling, command interpretation, or app flow belongs in:
  - `src/compiler/runtime.aos`

## Required Workflow

When adding behavior:

1. Add/adjust AOS logic first (`src/compiler/*.aos`, `src/std/*.aos`).
2. Only add a C# primitive if AOS cannot express the operation.
3. Keep new C# primitive generic and side-effect scoped.
4. Add/adjust goldens in `examples/golden`.

## Non-Goals for C#

- No HTTP app orchestration logic in C#.
- No lifecycle loops in C#.
- No command routing semantics in C#.

If a change needs these, implement it in AOS and keep C# as syscall-only transport.

## VM Purity Invariant (New)

AiVM is a deterministic execution microkernel.

The VM core must:
- Execute bytecode as a pure state transition engine.
- Perform no host IO, time access, randomness, or process inspection directly.
- Contain no lifecycle, routing, HTTP, or orchestration logic.
- Make no semantic decisions beyond instruction execution.

All observable side effects must occur exclusively through explicit `sys.*` invocations.

Syscalls form the only boundary between deterministic VM execution and host effects.
