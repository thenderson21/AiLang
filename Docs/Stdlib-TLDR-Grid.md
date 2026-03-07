# AiLang Libraries TLDR Grid

This is the short version of what AiLang needs from its first-party libraries
to behave like a production programming language.

## Core library grid

| Area | Module | Required for baseline | Why it exists | Immediate need |
|---|---|---:|---|---|
| Core results/assertions | `std.core` | Yes | Shared result nodes, assertions, basic structural helpers | Keep and harden as common foundation |
| Console IO | `std.io` | Yes | Normal app output without direct `sys.*` use | Expand beyond print-only if needed |
| Strings | `std.str` | Yes | Deterministic text operations | Keep as canonical text helper surface |
| Bytes | `std.bytes` | Yes | Binary payload handling for files, network, encoding | Keep and test heavily |
| Math | `std.math` | Yes | Minimal arithmetic helpers and future numeric growth point | Expand intentionally, not ad hoc |
| JSON | `std.json` | Yes | API parse/serialize baseline | Harden contract and coverage |
| System metadata | `std.system` | Yes | Platform, arch, runtime identity | Keep as single canonical system surface |
| Process | `std.process` | Yes | Args, env, cwd, child process control | Keep minimal and capability-bound |
| Filesystem | `std.fs` | Yes | File and path operations for real apps/tools | Keep minimal, deterministic, capability-bound |
| Time | `std.time` | Yes | Time reads and sleep primitives | Keep explicit and target-documented |
| Debugging | `std.debug` | Yes | Production diagnostics, tracing, replay, capture | Treat as first-class production library |
| Network | `std.net` | Yes | Low-level network baseline under `std.*` | Keep mechanical, not convenience-heavy |
| HTTP | `std.http` | Yes | Canonical app-facing HTTP layer | Make this the normal API surface for services/clients |
| UI input | `std.ui_input` | No | Useful UI helpers for text/edit/event handling | Keep optional and profile-specific |
| Platform alias | `std.platform` | No | Redundant wrapper over `std.system` | Remove, no aliasing |

## Non-stdlib surfaces

| Area | Namespace | Baseline library surface | Rule |
|---|---|---:|---|
| Host capability boundary | `sys.*` | No | Keep low-level; app code should prefer `std.*` |
| Toolchain/compiler internals | `compiler.*` | No | Not an app library namespace |

## What is still needed

| Need | Why it matters | Current direction |
|---|---|---|
| One canonical app-facing namespace | Prevents `std.*`/`compiler.*` drift | Make `std.*` the only intended app library surface |
| No aliases / no fallback names | Avoids bad contract debt before `1.0.0` | Remove duplicate modules instead of preserving wrappers |
| Per-target capability matrix | Native/wasm support differs | Document support and failure mode per baseline module |
| Conformance tests | Docs alone are not enough | Add tests that enforce required modules and exports |
| Sample adoption | Samples define the real developer experience | Move samples to `std.*`, not `sys.*` |
| Production debug story | Language must be debuggable in production | Keep `std.debug` baseline and testable |
| Hardened HTTP/JSON | Real apps depend on these first | Review contract, errors, and deterministic behavior |

## Decision summary

| Question | Answer |
|---|---|
| Is `std.debug` baseline? | Yes |
| Is `std.ui_input` baseline? | No |
| Is `std.platform` baseline? | No |
| Should `std.platform` remain as alias? | No |
| Should pre-`1.0.0` preserve compatibility aliases? | No |
| Should normal apps use `compiler.*`? | No |
| Should normal apps use `sys.*` directly? | Prefer not to; use `std.*` unless building host-boundary libraries |
