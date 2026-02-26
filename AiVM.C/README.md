# AiVM.C

## Purpose

`AiVM.C` initializes a clean C rewrite scaffold of the AiVM deterministic core. This branch is structural scaffolding only and is not a feature-complete VM port.

## Deterministic VM Goal

The VM core is a pure state transition engine:

- deterministic instruction dispatch
- explicit VM state container
- no hidden side effects
- no global mutable state
- no time, randomness, threads, or OS calls in VM core

## Host Separation

The VM does not implement host behavior directly. Syscalls are external and invoked through a typed handler function pointer. This keeps the host mechanical and preserves syscall boundary clarity.

## Why C

C provides a thin, portable, embeddable runtime foundation:

- straightforward embedding across host environments
- no managed runtime dependency in the VM core
- explicit control over memory ownership and state flow

## Semantics Authority

AiLang semantics remain governed by the AiLang specification (`SPEC/IL.md`, `SPEC/EVAL.md`, `SPEC/VALIDATION.md`).

This scaffold does not introduce new language semantics or runtime behavior.
