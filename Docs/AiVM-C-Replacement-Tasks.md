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
Status: `completed`
Goal: 100% parity dashboard across canonical corpus and runtime entrypoints.
Current: 66/66 canonical corpus pass (`100%`), with strict native entrypoint checks.
Exit: parity dashboard fully green, no known semantic drift.

2. `EPIC-ZC2` C Runtime As Sole Engine
Status: `in_progress`
Goal: remove remaining bridge-gated transitional runtime behavior and C# runtime fallback semantics.
Current: `--vm=c` run-source/bytecode/bundle parity paths are active through native shared runtime loading; remaining blocker is `serve --vm=c`, which still routes to host-side `DEV008` backend-not-linked behavior.
Exit: runtime-only C path for run-source, embedded-bytecode, embedded-bundle, and serve.
Note: native `AiCLI` wrapper stage is now present (`src/AiCLI/native/airun.c`), but still delegates non-`run --vm=c` flows to the legacy backend host binary.

3. `EPIC-ZC3` Repo-wide C# Deletion
Status: `completed`
Goal: remove all C# projects and C# test/tooling dependencies from mainline.
Exit: no dotnet requirement in mainline build/test workflows.

4. `EPIC-ZC4` Compiler Benchmarking + Regression Gates
Status: `completed`
Goal: benchmark compiler/runtime with frozen baselines and CI regression gates.
Current: benchmark gate integrated in dashboard with threshold checks.
Exit: benchmark suite + baseline + CI threshold enforcement.

5. `EPIC-ZC5` Sample Program Production Completion
Status: `completed`
Goal: all sample apps reach completion bar (functional + determinism + perf + memory).
Current: sample manifest shows all tracked samples complete.
Exit: all samples marked complete and pass all sample gates.

6. `EPIC-ZC6` Memory Management + Leak Tooling
Status: `completed`
Goal: deterministic RC + cycle collector + leak/profiling tooling.
Exit: memory/leak suite integrated and green in CI.

## Native Source Layout

Canonical post-cutover source layout remains under `src/`:

- `src/AiLang.Core` (native implementation target under `src/AiLang.Core/native`)
- `src/AiVM.Core` (native implementation rooted at `src/AiVM.Core/native`)
- `src/AiCLI` (native implementation target under `src/AiCLI/native`)

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

## Local Reproducibility Runbook (M4)

Use this sequence to reproduce threading/task readiness checks locally with deterministic outputs.

### Preconditions

- Run from repository root.
- `tools/airun` exists and is executable.
- No network dependency is required.

### Command Sequence

1. Core deterministic test gate:

```bash
./test.sh
```

Expected:

- `100% tests passed` for `src/AiVM.Core/native` C test suite.
- `parity dashboard: 97/97 passing (100.00%)` (or current canonical corpus count at run time).
- `overall DoD status: PASS`.

2. Determinism-only focused check:

```bash
ctest --test-dir .tmp/aivm-c-build-native -R aivm_test_vm_determinism
```

Expected:

- `aivm_test_vm_determinism` passes.

3. Dashboard gate refresh:

```bash
./scripts/aivm-parity-dashboard.sh .tmp/aivm-parity-dashboard-local.md
```

Expected in report:

- `Behavioral parity` = `PASS`.
- `Task tooling` = `PASS`.
- `Determinism readiness` = `PASS` (or `PENDING` only when tests are intentionally skipped via env flags).

4. Task-tooling parity edge case presence:

```bash
rg -n "parity_vm_c_execute_src_(await_edge_invalid|par_join_edge_invalid|par_cancel_edge_noop)" src/AiVM.Core/native/tests/parity_commands_portable.txt
```

Expected:

- Exactly 3 matching entries.

### Failure Triage Order

1. If determinism gate fails, fix `aivm_test_vm_determinism` before parity manifest updates.
2. If task-tooling gate fails with missing edge cases, restore entries in parity command manifests first.
3. If full test gate fails after parity edits, run changed parity cases directly with `./tools/airun run <case>.aos --vm=c` and reconcile `.out` snapshots.
