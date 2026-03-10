# PR: Memory + GC Hardening

## Scope

This PR hardens deterministic memory behavior for AiVM C runtime and increases memory/GC observability for CI and release gates.

## Strategy

- Runtime memory model remains deterministic and bounded.
- Node values use deterministic tracing compaction under fixed policy.
- RC-style ownership is reserved for host boundary resources only; VM-visible semantics remain deterministic.

## Included Changes

1. Added per-arena pressure counters and deterministic reset behavior.
2. Added deterministic node-GC policy telemetry in debug artifacts.
3. Added `node_gc_attempts` counter to separate attempt volume from compaction success.
4. Made memory/GC telemetry counters saturating to avoid wraparound.
5. Expanded VM and smoke tests for:
- pressure error paths
- reset invariants
- success-path zero-pressure invariants
- GC trigger boundaries and hard-cap behavior
6. Strengthened CI/release leak gates:
- default `AIVM_LEAK_CHECK_ITERATIONS=50`
- default `AIVM_LEAK_MAX_GROWTH_KB=2048`
- failed-run leak report artifact upload
7. Added process lifecycle fail-path cleanup stress coverage:
- repeated nonzero-exit `spawn -> wait -> poll` loops

## Files of Interest

- `src/AiVM.Core/native/src/aivm_vm.c`
- `src/AiVM.Core/native/include/aivm_vm.h`
- `src/AiCLI/native/airun.c`
- `src/AiVM.Core/native/tests/test_vm_core.c`
- `src/AiVM.Core/native/tests/test_vm_ops.c`
- `src/AiVM.Core/native/tests/test_process_lifecycle_stress.c`
- `scripts/test-aivm-c.sh`
- `.github/workflows/aivm-c-ci.yml`
- `.github/workflows/main-release-gate.yml`
- `.github/workflows/toolkit-release.yml`
- `SPEC/EVAL.md`
- `Docs/Production-Memory-Readiness.md`

## Validation

- `./test.sh` passes.
- Native C test suite passes including:
- `aivm_test_vm_core`
- `aivm_test_vm_ops`
- `aivm_test_process_lifecycle_stress`
- Parity dashboard gate remains green.

## Risk Notes

- Leak gate defaults are stricter and may surface baseline differences on slower or noisier runners.
- Additional process lifecycle stress loops increase one test runtime slightly.
