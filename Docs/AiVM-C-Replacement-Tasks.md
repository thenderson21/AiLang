# AiLang Zero-C# Completion Tasks

## Objective

Finish migration to a full AiLang project without C# in mainline, with behavioral parity at least equal to the former C# baseline and explicit quality gates for tests, benchmarks, samples, and memory/leak behavior.

## Hard Definition Of Done

All gates below must be green at the same time:

1. Behavioral parity: `100%` pass on canonical parity corpus.
2. Zero C#: no tracked `.cs`, `.csproj`, `.sln`, `.slnx` files in mainline.
3. Test coverage: full required suite passes on macOS/Linux/Windows.
4. Benchmark: compiler/runtime benchmark gates pass (no regressions > threshold).
5. Samples: all repo samples marked complete and passing.
6. Memory: RC invariants, cycle collector tests, and leak checks pass.

No partial completion state counts as done.

## Scope Decisions

- No C# scope: repo-wide zero C#.
- Memory model: deterministic reference counting + deterministic cycle collection passes.
- Finished program scope: all repo samples.

## Epic Tracker

1. `EPIC-ZC1` Runtime Behavioral Parity Closure
Status: `in_progress`
Goal: 100% parity dashboard across canonical corpus and runtime entrypoints.
Current: 18/66 pass (`27.27%`) in execute mode.
Exit: parity dashboard fully green, no known semantic drift.

2. `EPIC-ZC2` C Runtime As Sole Engine
Status: `in_progress`
Goal: remove remaining bridge-gated transitional runtime behavior and C# runtime fallback semantics.
Current: C path exists but transitional gate behavior still present in selected entrypoints/diagnostics.
Exit: runtime-only C path for run-source, embedded-bytecode, embedded-bundle, and serve.

3. `EPIC-ZC3` Repo-wide C# Deletion
Status: `pending`
Goal: remove all C# projects and C# test/tooling dependencies from mainline.
Exit: no dotnet requirement in mainline build/test workflows.

4. `EPIC-ZC4` Compiler Benchmarking + Regression Gates
Status: `in_progress`
Goal: benchmark compiler/runtime with frozen baselines and CI regression gates.
Current: `airun bench` exists; no frozen non-regression gate integrated yet.
Exit: benchmark suite + baseline + CI threshold enforcement.

5. `EPIC-ZC5` Sample Program Production Completion
Status: `in_progress`
Goal: all sample apps reach completion bar (functional + determinism + perf + memory).
Current: sample inventory exists; completion manifest/gates not all green.
Exit: all samples marked complete and pass all sample gates.

6. `EPIC-ZC6` Memory Management + Leak Tooling
Status: `pending`
Goal: deterministic RC + cycle collector + leak/profiling tooling.
Exit: memory/leak suite integrated and green in CI.

## Issue Requirements (Mandatory Fields)

All new migration issues must include:

- Behavioral contract reference (`SPEC/*`)
- Determinism impact
- Parity case(s)
- Memory impact (alloc/free path touched)
- Acceptance test IDs

## Labels

- `parity`
- `zero-csharp`
- `gc`
- `memory-leak`
- `bench`
- `samples`
- `ci-gate`
- `spec-impact`

## Milestones

- `M1 Parity 100`
- `M2 C-only runtime`
- `M3 Zero C# repo`
- `M4 Memory + Benchmark done`

## Execution Order

1. Close parity from 18/66 to 66/66.
2. Remove runtime transitional fallback behavior; keep AST debug-only.
3. Remove repo-wide C# from mainline.
4. Complete RC+cycle memory model and leak/profiling.
5. Lock benchmark gates and finish all samples.

