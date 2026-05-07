# agents.md — AI Operating Rules

This repository is an AI-native language runtime.
The AI must treat architectural constraints as hard rules.

## Prime Directive

AiLang exists to let AI agents create, understand, modify, debug, and ship software for any target from high-level intent, with humans acting mainly as prioritizers and approvers.

## Tooling Boundary

- Agents must prefer improving AiLang and AiVectra built-in capabilities, diagnostics, debug surfaces, and automation over forcing progress with inadequate tooling.
- If a task cannot be completed cleanly with the current toolchain, the correct next step is to extend the toolchain rather than normalize manual or external workarounds.
- Human verification is exception-only. If agents repeatedly need a human to confirm what the runtime or UI shows, treat that as a tooling gap to be closed.
- Agent-visible debug artifacts should match user-visible reality under the same build, runtime, input, and target conditions.

## Non-negotiable constraints

- NO external libraries or NuGet packages.
- NO JSON usage anywhere (input, output, internal), except explicit host boundary adapters for client-facing integrations (for example HTTP/Web API responses) when approved by task requirements.
- ONLY the AI-Optimized Syntax (AOS) is allowed.
- Canonical execution engine is AiBC1 VM; AST interpreter is debug-only (`--vm=ast`).
- The interpreter, validator, and REPL must remain deterministic.
- No network, filesystem writes, time, or randomness unless explicitly via capability and permission.
- Stable node IDs are required; never regenerate IDs unnecessarily.
- Semantic IR is the source of truth. Encoding is not.
- AiVM must remain a deterministic state transition engine.
- No hidden side effects inside the VM; all effects must route through `sys.*`.
- VM execution must not directly access time, randomness, network, filesystem, or process state.
- Syscalls are the only permitted escape hatch from deterministic execution.
- Do not request or add new `sys.*` targets for deterministic language or
  library behavior such as string replacement, collection operations, template
  rendering, parsing, validation, compiler policy, or compatibility adapters.
  Those belong in AiLang or AiLang core libraries.
- A new syscall is only appropriate when it crosses a host boundary and cannot
  be implemented deterministically in AiLang.

## Layer boundaries (enforced)

- `src/AiLang.Core` is language-only:
  - parser/tokenizer bridge
  - AST/IR structures
  - validator and deterministic language semantics
  - no direct syscall, network, file, or process operations
- `../AiVM/native` is VM-only:
  - AiBC1 loading/execution
  - deterministic state transition engine
  - syscall dispatch boundary only (`sys.*`)
  - no language-spec ownership changes without `SPEC/` updates
  - new syscall contracts require `../AiVM/Docs/Syscalls.md` justification and
    AiVM contract tests
- `src/AiVectra` is UI-layer placeholder (no active runtime integration yet).

## What the AI is allowed to do

- Implement tokenizer, parser, validator, interpreter, REPL.
- Add new IR node kinds ONLY if required by tests or examples.
- Refactor internal code if behavior and tests remain identical.
- Add tests when adding features.
- Improve performance without changing semantics.

## What the AI must not do

- Do not invent a new syntax.
- Do not add a human-friendly surface language.
- Do not bypass the capability system.
- Do not introduce hidden side effects.
- Do not weaken validation or type checking.
- Do not silently change output formatting.
- Do not add C VM/runtime/native launcher code to AiLang. VM/runtime/native
  launcher C belongs in AiVM, and foreign C library access must go through an
  explicit SDK/syscall/adapter boundary. The temporary `tools/aos_frontend.c`
  parser bootstrap is allowed until the parser frontend is rewritten in AiLang.

## Development workflow

- Work in small, reviewable changes.
- Run `./test.sh` frequently.
- Keep diffs minimal and focused.
- Prefer editing existing code over rewriting files.
- When unsure, stop and ask for clarification.

## Local commands

- Use `./tools/ailang` for day-to-day execution.
- VM is default for `run`; use `--vm=ast` only for debugging unsupported bytecode paths.
- Production runtime builds (`AosDevMode=false`) disable `--vm=ast` and source-mode commands.
- Use `./build.sh` or `./build.ps1` as the canonical tooling bootstrap entrypoint.
- Use `./test.sh` or `./test.ps1` as the canonical verification entrypoint.
- Treat `scripts/test*.sh` and `scripts/test*.ps1` as internal implementation details behind the canonical verification entrypoint unless the task is specifically about test-harness maintenance.
- Treat `scripts/build-*.sh` and `scripts/build-*.ps1` as internal implementation details behind the canonical bootstrap entrypoint unless the task is specifically about build-script maintenance.
- Do not use `dotnet run` or `dotnet test` for normal workflow.
- Frontend parsing currently uses temporary bootstrap tool `tools/aos_frontend`.
- Native launchers and host adapters are supplied by the selected installed SDK.

## Definition of done

A change is complete only if:
- All tests pass.
- Behavior is deterministic.
- Output matches canonical formatting.
- No architectural rules are violated.

## Normative specs

- `SPEC/IL.md`, `SPEC/EVAL.md`, and `SPEC/VALIDATION.md` are language contracts.
- Semantic changes must update spec docs and matching goldens in the same change.

## Release Contract Policy

IMPORTANT: Until a major or minor release is officially released, all contracts,
APIs, schemas, interfaces, and architectural decisions are considered
negotiable and may change freely. Do not add backward compatibility layers,
legacy adapters, or dual-path support unless explicitly requested. When changing
direction, replace the old implementation completely and update the codebase
consistently to the new contract. Patch releases are for bug fixes only.

## Documentation policy

- Root `README.md` is human-oriented.
- `Docs/README.md` and files under `Docs/` are agent-oriented.
- Agents should prefer `Docs/` + `SPEC/` for operational guidance.
- This repository should be treated as an AiLang project with manifest `project.aiproj`.

## Concurrency and Event Model (Authoritative)

AiLang defines concurrency semantics. AiVectra does not.

Ownership
	•	AiLang defines:
	•	spawn
	•	message passing
	•	event handling semantics
	•	single semantic thread authority
	•	AiVM implements:
	•	worker thread pool (mechanical only)
	•	deterministic event queue primitive
	•	thread scheduling mechanics
	•	AiVectra integrates with the AiLang event system but must not define concurrency primitives.

Single Semantic Thread Rule

All language-level state mutation must occur on the single semantic thread.

Worker threads:
	•	May perform blocking IO or long-running computation.
	•	Must never mutate semantic state directly.
	•	Must communicate only through the deterministic event queue.

Event Queue Authority

The core event queue is part of AiVM.

Libraries (including AiVectra):
	•	May enqueue events.
	•	Must not implement independent event systems.
	•	Must not bypass the VM event queue.

UI Independence

AiLang must remain UI-agnostic.

AiLang:
	•	Must not depend on AiVectra.
	•	Must not introduce UI semantics into language rules.
	•	Must not embed rendering logic.

If a feature can logically exist without UI, it belongs in AiLang or AiVM.
