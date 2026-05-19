# AiLang Libraries TLDR Grid

This is the short version of what AiLang needs from its first-party libraries
to behave like a production programming language.

## Core library grid

| Area | Module | Required for baseline | Why it exists | Immediate need |
|---|---|---:|---|---|
| Core results/assertions | `std.core` | Yes | Shared result nodes, assertions, basic structural helpers | Keep and harden as common foundation |
| Console IO | `std.io` | Yes | Normal stdout/stderr output without direct `sys.*` use | Keep stdout/stderr minimal and portable |
| Strings | `std.str` | Yes | Deterministic text operations | Keep as canonical text helper surface |
| Bytes | `std.bytes` | Yes | Binary payload handling for files, network, encoding | Keep and test heavily |
| Null | `std.null` | Yes | Standard null construction, checks, and coalescing | Keep small and deterministic |
| Numbers | `std.number` | Yes | Integer helpers, comparison, parsing, formatting, and ranges | Keep integer-first until numeric error policy is settled |
| Math | `std.math` | Yes | Minimal arithmetic helpers and future numeric growth point | Current baseline is add/sub/mul/negate; defer div/mod |
| System metadata | `std.system` | Yes | Platform, arch, runtime identity | Keep as single canonical system surface |
| Process | `std.process` | Yes | Args, env, cwd, child process control | Keep minimal and capability-bound |
| Filesystem | `std.fs` | Yes | File and path operations for real apps/tools | Keep minimal, deterministic, capability-bound |
| Time | `std.time` | Yes | Time reads and sleep primitives | Keep explicit and target-documented |
| Debugging | `std.debug` | Yes | Production-safe diagnostics only | Mode, stderr logging, and assertions only |
| Platform alias | `std.platform` | No | Redundant wrapper over `std.system` | Remove, no aliasing |

## Non-stdlib surfaces

| Area | Namespace | Baseline library surface | Rule |
|---|---|---:|---|
| Host capability boundary | `sys.*` | No | Keep low-level; app code should prefer `std.*` |
| Toolchain/compiler internals | `compiler.*` | No | Not an app library namespace |

## Namespace matrix

| Area | Recommended namespace | Purpose |
|---|---|---|
| Generic text utilities | `std.text` | Generic string/text helpers shared across domains |
| JSON | `std-json` package | JSON parse, serialize, and JSON node helpers |
| XML | `std.xml` | XML parse, serialize, and XML node helpers |
| TOML | `std.toml` | TOML parse, serialize, and config helpers |
| Markdown | `std.markdown` | Markdown parse/render/document helpers |
| Bytes/binary | `std.bytes` | Binary payload handling |
| HTTP | `std-http` package | HTTP protocol/client/server helpers |
| Raw networking | `std-net` package | Lower-level networking primitives |
| Filesystem | `std.fs` | File/path operations |
| Process/runtime | `std.process` | Args, env, and child process control |
| System metadata | `std.system` | Platform, arch, and runtime identity |
| Time | `std.time` | Time and sleep primitives |
| Debugging | `std.debug` | Production-safe diagnostics: mode, logging, assertions |
| Core helpers | `std.core` | Results, assertions, and shared structural helpers |
| Console IO | `std.io` | Standard input/output helpers |
| Math | `std.math` | Numeric helpers |

## Shared parser internals

| Internal shared area | Suggested namespace | Public surface |
|---|---|---|
| Parser cursor/token helpers | `std.parse_core` or `std.text.parse` | No |
| Escape/unicode helpers | `std.text` or internal parse module | Usually no |
| Shared parse result/error builders | `std.core` or internal parse module | Usually no |

## What is still needed

| Need | Why it matters | Current direction |
|---|---|---|
| One canonical app-facing namespace | Prevents `std.*`/`compiler.*` drift | Make `std.*` the only intended app library surface |
| No aliases / no fallback names | Avoids bad contract debt before `1.0.0` | Remove duplicate modules instead of preserving wrappers |
| Per-target capability matrix | Native/wasm support differs | Document support and failure mode per baseline module |
| Conformance tests | Docs alone are not enough | Enforce required modules, exports, capability rows, and deterministic behavior |
| Sample adoption | Samples define the real developer experience | Move samples to `std.*`, not `sys.*` |
| Production debug story | Language must be diagnosable in production without carrying the full debug runtime | Keep `std.debug` limited and testable; put capture/replay in debug/profile tooling |
| Hardened package libraries | Real apps depend on these first | Move optional HTTP/JSON/network/UI helpers through packages with clear contracts |

## Decision summary

| Question | Answer |
|---|---|
| Is `std.debug` baseline? | Yes, but limited to mode, logging, and assertions |
| Are capture/replay/artifact APIs baseline? | No, they belong to debug/profile tooling or optional packages |
| Are `std.null` and `std.number` baseline? | Yes |
| Are division and modulo baseline? | No, not until numeric failure/result semantics are specified |
| Is `std.ui_input` baseline? | No, it belongs in an optional package or AiVectra |
| Are `std.http`, `std.json`, `std.net`, and `std.image` baseline? | No, they belong in optional packages |
| Is `std.platform` baseline? | No |
| Should `std.platform` remain as alias? | No |
| Should pre-`1.0.0` preserve compatibility aliases? | No |
| Should normal apps use `compiler.*`? | No |
| Should normal apps use `sys.*` directly? | Prefer not to; use `std.*` unless building host-boundary libraries |
