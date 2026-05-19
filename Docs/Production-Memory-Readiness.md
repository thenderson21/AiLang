# Production Memory Readiness

## Objective

Define a strict, testable memory/GC bar for production readiness of AiLang/AiVM.

This document is an execution checklist, not a roadmap narrative.

## Current Baseline

- VM memory is deterministic and bounded by explicit capacities in `../AiVM/native/include/aivm_vm.h`.
- Large VM regions are heap-backed inside AiVM instead of embedded directly in
  the `AivmVm` struct.
- Node graph memory uses deterministic tracing compaction with fixed policy:
  - `node_gc_interval_allocations = 64`
  - `node_gc_pressure_threshold_nodes = 12288`
  - `node_gc_pressure_threshold_attrs = 49152`
  - `node_gc_pressure_threshold_children = 98304`
  - hard-cap path compacts before emitting `AIVMM005`.
- Memory pressure telemetry is emitted in debug artifacts:
  - `string_arena_pressure_count`
  - `bytes_arena_pressure_count`
  - `node_arena_pressure_count`
  - `node_gc_attempts`
- Root-attribution telemetry is emitted in debug artifacts:
  - flat `node_root_*` counters in `state_snapshots.toml`
  - structured `node_roots` table in `diagnostics.toml`
- Node-kind attribution is emitted in debug artifacts:
  - `node_kind_counts` in `state_snapshots.toml`
  - `node_kind_counts` in `diagnostics.toml`
- Stability checks exist (`test_memory_rc.c`, `test_memory_cycle.c`).
- Leak/profile scripts exist:
  - `scripts/aivm-mem-leak-check.sh`
  - `scripts/aivm-mem-profile.sh`
  - `scripts/profile-parser-memory.sh`
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
- `ailang debug profile` emits deterministic TOML (`aivm_debug_mem_v1`) and is exercised through `scripts/aivm-mem-audit.sh`.
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
- Main release gate includes benchmark regression checks.
- Gate result is reflected in parity dashboard output.

## Suggested Gate Defaults

- `AIVM_LEAK_MAX_GROWTH_KB=2048` for baseline checks.
- `AIVM_LEAK_CHECK_ITERATIONS=50` for fast CI.
- `AIVM_LEAK_CHECK_ITERATIONS=500+` for release candidate validation.

## Immediate Next Tasks

1. Fix parser intermediate retention before raising VM node limits again.
2. Add one deterministic async/process cleanup stress test focused on cancel/fail paths.
3. Expand memory-growth audit targets beyond the single baseline parity case.
4. Keep release-gate assertions on debug bundle memory/root/kind-attribution telemetry fields.

## Parser Memory Finding

`scripts/profile-parser-memory.sh src/compiler/format.aos` currently reaches
`AIVMM005` with all `16384` node slots retained from locals. The diagnostic
bundle reports one `Block` retaining more than sixteen thousand children and a
large `unknown` node-kind bucket. This points at parser intermediate
representation/lifetime behavior, not a need for another arena capacity
increase.
