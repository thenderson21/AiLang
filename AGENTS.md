# agents.md — AI Operating Rules

This repository is an AI-native language runtime.
The AI must treat architectural constraints as hard rules.

## Non-negotiable constraints

- NO external libraries or NuGet packages.
- NO JSON usage anywhere (input, output, internal), except explicit host boundary adapters for client-facing integrations (for example HTTP/Web API responses) when approved by task requirements.
- ONLY the AI-Optimized Syntax (AOS) is allowed.
- Canonical execution engine is AiBC1 VM; AST interpreter is debug-only (`--vm=ast`).
- The interpreter, validator, and REPL must remain deterministic.
- No network, filesystem writes, time, or randomness unless explicitly via capability and permission.
- Stable node IDs are required; never regenerate IDs unnecessarily.
- Semantic IR is the source of truth. Encoding is not.

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

## Development workflow

- Work in small, reviewable changes.
- Run `./scripts/test.sh` frequently.
- Keep diffs minimal and focused.
- Prefer editing existing code over rewriting files.
- When unsure, stop and ask for clarification.

## Local commands

- Use `./tools/airun` for day-to-day execution.
- VM is default for `run`; use `--vm=ast` only for debugging unsupported bytecode paths.
- Production runtime builds (`AosDevMode=false`) disable `--vm=ast` and source-mode commands.
- Use `./scripts/test.sh` for golden test validation.
- Use `./scripts/build-airun.sh` only when rebuilding `tools/airun` via dotnet publish.
- Do not use `dotnet run` or `dotnet test` for normal workflow.
- Frontend parsing is provided by standalone `tools/aos_frontend`.

## Definition of done

A change is complete only if:
- All tests pass.
- Behavior is deterministic.
- Output matches canonical formatting.
- No architectural rules are violated.

## Normative specs

- `SPEC/IL.md`, `SPEC/EVAL.md`, and `SPEC/VALIDATION.md` are language contracts.
- Semantic changes must update spec docs and matching goldens in the same change.

## Documentation policy

- Root `README.md` is human-oriented.
- `Docs/README.md` and files under `Docs/` are agent-oriented.
- Agents should prefer `Docs/` + `SPEC/` for operational guidance.
- This repository should be treated as an AiLang project with manifest `project.aiproj`.
