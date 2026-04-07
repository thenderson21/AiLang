# Production Memory Readiness

## Objective

Define a strict, testable memory/GC bar for production readiness of AiLang/AiVM.

This document is an execution checklist, not a roadmap narrative.

## Current Baseline

- VM memory is deterministic and bounded by explicit capacities in `src/AiVM.Core/native/include/aivm_vm.h`.
- Node graph memory uses deterministic tracing compaction with fixed policy:
  - `node_gc_interval_allocations = 64`
  - `node_gc_pressure_threshold_nodes = 384`
  - `node_gc_pressure_threshold_attrs = 1536`
  - `node_gc_pressure_threshold_children = 3072`
  - hard-cap path compacts before emitting `AIVMM005`.
- Memory pressure telemetry is emitted in debug artifacts:
  - `string_arena_pressure_count`
  - `bytes_arena_pressure_count`
  - `node_arena_pressure_count`
  - `node_gc_attempts`
- Stability checks exist (`test_memory_rc.c`, `test_memory_cycle.c`).
- Leak/profile scripts exist:
  - `scripts/aivm-mem-leak-check.sh`
  - `scripts/aivm-mem-profile.sh`
- Dashboard currently reports Memory/GC pass in `Docs/AiVM-C-Parity-Status.md`.

## Production Strategy (Recommended)

1. Keep deterministic arena ownership inside AiVM as the default runtime model.
2. Use deterministic tracing compaction for graph/node values (already in VM policy).
3. Use explicit capability-bound handles for host resources (file/process/network), with deterministic release points.
4. If RC is introduced for host-side resources, treat it as a host-boundary mechanism only; language-visible semantics remain deterministic and VM-owned.
5. Keep all pressure/error paths typed (`AIVMM*`) and observable via debug telemetry.

## Exit Criteria (Production-Grade)

1. Spec Contract Locked
- Memory ownership/lifetime rules are explicit in `SPEC/`.
- OOM and limit overflow behavior has deterministic error codes.
- String/bytes/node lifetime semantics are documented and test-backed.

2. Leak Detection Is CI-Enforced
- Leak check runs on Linux and macOS in CI.
- Growth threshold is enforced by gate (fail on regression).
- Report artifacts are retained for failed runs.

3. Long-Run Stress Coverage
- Add deterministic stress suites for:
  - CLI loops
  - process/syscall-heavy paths
  - async task completion/cancel loops
- Minimum high-iteration run is part of release gate.

4. Cycle/Retention Coverage
- Cycle tests include nested/mixed graphs and error-path cleanup.
- Cancel/fail/timeout cleanup is verified for async/process handles.
- No stale-handle growth over repeated runs.

5. Cross-Platform Observability
- Memory profile output has consistent fields across supported platforms.
- Peak RSS and growth trend are machine-parseable in artifacts.

6. Release Gate Integration
- Main release gate includes memory checks.
- Gate result is reflected in parity dashboard output.

## Suggested Gate Defaults

- `AIVM_LEAK_MAX_GROWTH_KB=2048` for baseline checks.
- `AIVM_LEAK_CHECK_ITERATIONS=50` for fast CI.
- `AIVM_LEAK_CHECK_ITERATIONS=500+` for release candidate validation.

## Immediate Next Tasks

1. Wire `scripts/aivm-mem-leak-check.sh` into CI with threshold env vars and artifact retention.
2. Add one deterministic async/process cleanup stress test focused on cancel/fail paths.
3. Add release-gate assertions that debug bundle memory telemetry fields remain present and stable.
