# Threading/Task Tooling Plan (Local-Only)

Status: Completed execution plan for AiLang/AiVM tooling only.
Scope excludes AiVectra runtime implementation.

Completion snapshot (2026-03-05):

- M1 contracts/spec sync: complete.
- M2 task manager tooling surface: complete for current deterministic host/runtime boundaries.
- M3 worker bridge + host tooling contracts/docs: complete.
- M4 local stress/determinism/parity/dashboard/runbook: complete.

Primary completion evidence:

- `src/AiVM.Core/native/tests/test_syscall_contracts.c`
- `src/AiVM.Core/native/tests/test_syscall.c`
- `src/AiVM.Core/native/tests/test_runtime.c`
- `src/AiVM.Core/native/tests/test_vm_determinism.c`
- `src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_await_edge_invalid.aos`
- `src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_par_join_edge_invalid.aos`
- `src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_par_cancel_edge_noop.aos`
- `./test-aivm-c.sh`
- `scripts/aivm-parity-dashboard.sh`
- `Docs/SyscallCoverageSummary.md`
- `Docs/AiVM-C-Replacement-Tasks.md`

## Constraints

- Local-only execution and validation.
- No network dependency for plan execution.
- No new language-level thread primitives.
- Deterministic owner-thread semantics remain authoritative.

## Milestone M1: Contracts Locked

Goal: Freeze deterministic threading/task contracts across specs and docs.

Tasks:
1. Align owner-thread and worker boundary language in `SPEC/EVAL.md`.
   - Target: `SPEC/EVAL.md`
   - DoD: owner-thread mutation rule, worker completion visibility, and deterministic ordering are explicit and non-contradictory.
2. Confirm task value and async shape contracts in `SPEC/IL.md`.
   - Target: `SPEC/IL.md`
   - DoD: `Task(handle=...)` and await/par semantics are explicitly represented.
3. Confirm async/par bytecode ordering and cancellation rules in `SPEC/BYTECODE.md`.
   - Target: `SPEC/BYTECODE.md`
   - DoD: `ASYNC_CALL`, `AWAIT`, `PAR_BEGIN/FORK/JOIN/CANCEL` deterministic behavior is fully stated.
4. Lock validator expectations for task/await/worker misuse diagnostics.
   - Target: `SPEC/VALIDATION.md`
   - DoD: deterministic diagnostic expectations for invalid async/task shapes are specified.
5. Sync implementation status matrix for task/thread tooling.
   - Target: `Docs/AiVM-C-Conformance-Matrix.md`
   - DoD: status rows for async/par and worker contracts match current implementation state.

Validation commands:
- `./test.sh`
- `rg -n "ASYNC_CALL|AWAIT|PAR_|worker_|owner thread|determin" SPEC Docs -S`

## Milestone M2: AiVM Task Manager Tooling Surface

Goal: Provide deterministic internal tooling APIs for task lifecycle management.

Tasks:
1. Define internal task record/state model and transition guards.
   - Targets: `src/AiVM.Core/native/aivm_vm.c`, `src/AiVM.Core/native/include/aivm_vm.h`
   - DoD: transitions are explicit and invalid transitions yield deterministic VM errors.
2. Add structured parent/child task lifecycle helpers.
   - Targets: `src/AiVM.Core/native/aivm_vm.c`, `src/AiVM.Core/native/aivm_runtime.c`
   - DoD: parent failure/cancel deterministically propagates to children.
3. Add deterministic ready-order merge helper (ascending handle or lexical slot contract).
   - Targets: `src/AiVM.Core/native/aivm_vm.c`, `src/AiVM.Core/native/aivm_parity.c`
   - DoD: join materialization order is stable across runs.
4. Add owner-thread mutation guard utility in runtime path.
   - Targets: `src/AiVM.Core/native/aivm_runtime.c`, `src/AiVM.Core/native/include/aivm_runtime.h`
   - DoD: all semantic state transitions route through guarded owner path.

Validation commands:
- `./test.sh`
- `./scripts/test-c-vm.sh`
- `rg -n "PAR_JOIN|AWAIT|task|owner" src/AiVM.Core/native src/AiVM.Core/native/tests -S`

## Milestone M3: Worker Bridge and Host Tooling (No UI Runtime Work)

Goal: Ensure host-side worker tooling contracts are available for both high-concurrency hosting and future UI hosts.

Tasks:
1. Finalize `sys.worker_*` contract behavior and return typing tests.
   - Targets: `src/AiVM.Core/native/sys/aivm_syscall_contracts.c`, `src/AiVM.Core/native/tests/test_syscall_contracts.c`
   - DoD: contract table and tests cover arity/type/return for start/poll/result/error/cancel.
2. Add deterministic polling/result terminal-state test matrix.
   - Targets: `src/AiVM.Core/native/tests/test_syscall.c`, `src/AiVM.Core/native/tests/test_runtime.c`
   - DoD: pending/completed/failed/canceled/unknown-handle paths are validated.
3. Add host readiness adapter interfaces for external event enqueue/drain.
   - Targets: `src/AiVM.Core/native/include/aivm_runtime.h`, `src/AiVM.Core/native/aivm_runtime.c`
   - DoD: API supports local host integration without introducing UI semantics.
4. Add docs for host integration contract and local usage.
   - Targets: `src/AiVM.Core/native/README.md`, `Docs/SyscallCoverageSummary.md`
   - DoD: clear integration path for web-host and UI-host adapters using same deterministic queue semantics.

Validation commands:
- `./test.sh`
- `./test-aivm-c.sh`
- `rg -n "sys.worker_|poll|result|cancel|event queue" src/AiVM.Core/native Docs -S`

## Milestone M4: Local Stress + Determinism Tooling

Goal: Provide local tooling to validate 1000+ concurrent host operations and deterministic replay.

Tasks:
1. Add local stress harness for high in-flight operation simulation.
   - Targets: `src/AiVM.Core/native/tests/test_runtime.c`, `src/AiVM.Core/native/tests/test_vm_determinism.c`
   - DoD: local test simulates 1000+ operation handles with deterministic completion merge checks.
2. Add parity cases for async/par edge ordering and cancellation.
   - Targets: `src/AiVM.Core/native/tests/parity_cases/*`, `src/AiVM.Core/native/tests/parity_commands*.txt`
   - DoD: parity suite includes representative await/join/cancel ordering cases.
3. Add local dashboard/check workflow for task tooling readiness.
   - Targets: `scripts/aivm-parity-dashboard.sh`, `Docs/AiVM-C-Parity-Status.md`
   - DoD: dashboard reports task-tooling sub-gate and determinism readiness.
4. Add reproducibility notes and runbook.
   - Targets: `Docs/AiVM-C-Replacement-Tasks.md`
   - DoD: one local runbook section for repeatable validation commands and expected outcomes.

Validation commands:
- `./test.sh`
- `./scripts/aivm-parity-dashboard.sh`
- `./scripts/aivm-c-perf-smoke.sh`

## Backlog (Post-M4)

1. Optional CLI diagnostics for task table snapshots in debug mode only.
   - Targets: `src/AiCLI/native/airun.c`, `src/AiVM.Core/native/aivm_runtime.c`
2. Optional expanded deterministic telemetry for event queue depth over time.
   - Targets: `src/AiVM.Core/native/aivm_runtime.c`, `Docs/AiVM-C-Conformance-Matrix.md`

## Release Readiness Checklist (Tooling Scope)

1. `./test.sh` passes.
2. Async/par determinism tests pass across repeated runs.
3. `sys.worker_*` contracts and error paths are fully tested.
4. Local stress harness validates 1000+ in-flight management deterministically.
5. No AiVectra runtime implementation added.
