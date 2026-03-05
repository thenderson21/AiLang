# Syscall Coverage Summary

Last updated: 2026-03-04

## Purpose

Track syscall and host-tooling readiness for deterministic AiLang/AiVM execution.

Scope is local-only tooling and contracts (no AiVectra runtime implementation).

## Current Worker/Task Tooling Coverage

Implemented and test-backed:

- Worker syscall contracts:
  - `sys.worker.start(taskName,payload) -> int`
  - `sys.worker.poll(workerHandle) -> int`
  - `sys.worker.result(workerHandle) -> string`
  - `sys.worker.error(workerHandle) -> string`
  - `sys.worker.cancel(workerHandle) -> bool`
- Contract validation coverage includes:
  - return types
  - arity/type errors
  - id-target lookup parity
- Deterministic terminal-state matrix coverage includes:
  - pending
  - completed
  - failed
  - canceled
  - unknown handle

Primary tests:

- `src/AiVM.Core/native/tests/test_syscall_contracts.c`
- `src/AiVM.Core/native/tests/test_syscall.c`
- `src/AiVM.Core/native/tests/test_runtime.c`
- `src/AiVM.Core/native/tests/test_bytes_host.c`

## Host Event Queue Adapter Coverage

Implemented in runtime host bridge:

- `aivm_runtime_host_enqueue_event(...)`
- `aivm_runtime_host_drain_events(...)`

Contract summary:

- Adapter is host-provided via `AivmRuntimeHostAdapter`:
  - `enqueue(context, event_name, payload)`
  - `drain(context, max_events, out_drained_count)`
- Owner-thread remains semantic authority.
- Worker/host threads may enqueue only through adapter callbacks.
- Drain is explicit and bounded (`max_events`) to keep host-side sequencing deterministic.
- Result statuses:
  - `AIVM_RUNTIME_HOST_EVENT_OK`
  - `AIVM_RUNTIME_HOST_EVENT_INVALID`
  - `AIVM_RUNTIME_HOST_EVENT_REJECTED`

Primary tests:

- `src/AiVM.Core/native/tests/test_runtime.c`

## Status Against Threading/Task Plan (M3)

- M3.1 `sys.worker_*` contract behavior + return typing: complete.
- M3.2 deterministic polling/result terminal-state matrix: complete.
- M3.3 host readiness enqueue/drain adapter interfaces: complete.
- M3.4 host integration docs + local usage path: complete.

## Remaining Focus (Next Milestone)

M4 local stress/determinism:

- 1000+ in-flight operation simulation in deterministic harness.
- additional parity cases for async/par edge ordering and cancellation.
- dashboard/runbook updates for repeatable local readiness checks.
