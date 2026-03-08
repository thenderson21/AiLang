# AiLang vs AiVectra Boundary

This document defines the ownership boundary between AiLang and AiVectra during
pre-`1.0.0` contract negotiation.

The purpose is practical:

- place work in the correct repository
- keep `std.*` growth out of UI framework policy
- keep AiVectra from accumulating generic language/runtime utilities

## Core rule

If a feature can logically exist without UI, it belongs in AiLang or AiVM.

If a feature is specifically about UI composition, widget behavior, layout, or
render-time presentation policy, it belongs in AiVectra.

## Ownership summary

| Area | Owner | Why |
|---|---|---|
| Language syntax, validation, type rules, semantics | AiLang | Language contract |
| Bytecode execution, determinism, event queue, syscall dispatch | AiVM | Runtime authority |
| `sys.*` contract surface | AiLang/AiVM | Only runtime may define syscall authority |
| Generic `std.*` libraries | AiLang | UI-independent application surface |
| Concurrency semantics | AiLang/AiVM | AiVectra must not define concurrency primitives |
| Widget libraries and UI composition helpers | AiVectra | UI framework concern |
| High-level rendering helpers built from primitive `sys.ui.*` | AiVectra | Composition policy above VM primitives |
| App-facing UI convenience wrappers | AiVectra | UI library layer |

## Syscall policy

AiLang and AiVM own syscall authority.

Rules:

- `sys.*` contracts are defined in AiLang/AiVM.
- AiVectra may consume UI syscalls to implement the UI library layer.
- AiVectra must not define new language/runtime semantics by wrapping host
  behavior into pseudo-language contracts.
- Normal non-UI application code should prefer `std.*` rather than direct
  `sys.*`.
- Direct syscall access may remain publicly possible in AiLang for low-level
  host-boundary code.

This split means:

- syscall availability, types, determinism, and target behavior are AiLang
  concerns
- syscall consumption for UI library implementation is an AiVectra concern

## What belongs in AiLang

The following belong in AiLang unless there is a strong reason otherwise:

- generic data structures and helpers
- strings, bytes, math, parsing, and serialization utilities
- JSON, HTTP, raw networking, filesystem, process, and time libraries
- debug capture and replay contracts
- image decode primitives
- task, worker, and event queue semantics
- target capability declarations and deterministic unsupported behavior

Reason:

- none of these require UI as a concept
- pushing them into AiVectra would violate the UI-independence rule

## What belongs in AiVectra

The following belong in AiVectra:

- widget sets
- focus policy and widget interaction behavior
- text input composition helpers
- layout helpers
- themed drawing helpers
- higher-level rendering abstractions assembled from primitive `sys.ui.*`
- UI-specific debug visualization built on runtime debug data

Reason:

- these are framework and composition concerns, not language/runtime concerns

## Direct syscall use in AiVectra

AiVectra may access UI syscalls to implement the UI layer.

That allowance is narrow:

- `sys.ui.*` and closely related UI/runtime-debug integration points are valid
  implementation dependencies for AiVectra
- generic non-UI helpers should not be reimplemented there just because
  AiVectra happens to need them
- if AiVectra needs a generic helper for app state, HTTP, parsing, bytes, or
  image/network transport, the missing capability belongs in AiLang

## Triage rules

Use these rules for issue placement:

1. If the problem is about determinism, syscall dispatch, target capability, VM
   state, memory management, task/event semantics, or host integration:
   place it in AiLang.
2. If the problem is about widget behavior, rendering composition, focus,
   layout, theme, or UI library ergonomics:
   place it in AiVectra.
3. If AiVectra can only solve the problem by inventing generic language or host
   utility helpers:
   move the capability request to AiLang.
4. If the runtime primitive already exists and only higher-level UI composition
   is missing:
   keep the work in AiVectra.

## Anti-patterns

These are boundary violations:

- putting generic parsing or HTTP helpers into AiVectra
- adding UI semantics to AiLang language rules
- creating AiVectra-local event or concurrency systems
- implementing app/business flow in host runtime code instead of `std.*` or app
  code
- preserving duplicate aliases across repos just to avoid choosing an owner

## Contract status

- Pre-`1.0.0`, this boundary is still negotiable.
- Negotiation should narrow ambiguity, not preserve overlapping ownership.
- If ownership is unclear, prefer the lower and more generic layer:
  AiLang/AiVM before AiVectra.
