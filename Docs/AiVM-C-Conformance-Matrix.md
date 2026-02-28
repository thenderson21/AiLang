# AiVM-C Conformance Matrix

## Scope

This matrix tracks `AiVM.C` parity work against the current AiLang contracts in:

- `SPEC/BYTECODE.md`
- `SPEC/IL.md`
- `SPEC/EVAL.md`
- `SPEC/VALIDATION.md`

Status keys:

- `implemented`: deterministic behavior exists in C runtime
- `in_progress`: partially implemented deterministic behavior; remaining parity work tracked
- `placeholder`: opcode/feature is explicitly rejected deterministically
- `pending`: not yet ported into C runtime surface

## Runtime Core

| Area | Status | Notes |
|---|---|---|
| AiBC1 binary header/section decode | implemented | Deterministic loader checks with explicit status mapping. |
| VM state container (stack/frames/locals/ip) | implemented | Explicit mutable state, no globals, no hidden effects. |
| Deterministic step/run loop | implemented | Switch-based dispatch, no reflection/computed goto. |
| VM diagnostics mapping | implemented | Stable deterministic code/message mapping in C layer. |
| VM error detail strings | in_progress | Deterministic detail channel added (`aivm_vm_error_detail`) for central failure paths, including contract-subcoded syscall details from `CALL_SYS` (`AIVMS004/AIVMC*`); opcode-by-opcode parity wording still being expanded. |
| Runtime bridge API | implemented | `aivm_execute_program*` and C ABI adapters present. |

## Opcode Surface

| Opcode Family | Status | Notes |
|---|---|---|
| `NOP`, `HALT`, `PUSH_INT`, `PUSH_BOOL`, `POP` | implemented | Basic control/data flow active in VM. |
| `STORE_LOCAL`, `LOAD_LOCAL`, `CONST` | implemented | Locals + constant-pool path wired. |
| `ADD_INT`, `EQ_INT`, `EQ` | implemented | Deterministic typed stack operations. |
| `JUMP`, `JUMP_IF_FALSE`, `CALL`, `RET`, `RETURN` | implemented | Deterministic frame/control transfer logic. |
| `STR_CONCAT`, `TO_STRING`, `STR_ESCAPE` | implemented | Uses fixed-capacity VM string arena (no heap). |
| `STR_SUBSTRING`, `STR_REMOVE`, `STR_UTF8_BYTE_COUNT` | implemented | Rune-aware/clamped semantics in VM tests. |
| `CALL_SYS` | implemented | Contract-checked dispatch via typed syscall bindings. |
| `ASYNC_CALL*`, `AWAIT`, `PAR_*` | in_progress | Deterministic scaffold semantics now implemented for `ASYNC_CALL`, `ASYNC_CALL_SYS`, `AWAIT`, and `PAR_BEGIN/FORK/JOIN/CANCEL`; bytecode/function-index parity details remain in-progress. |
| `NODE_*`, `ATTR_*`, `CHILD_*`, `MAKE_*` | in_progress | Implemented deterministic `NODE_*`, `ATTR_*`, `CHILD_*`, `MAKE_BLOCK`, `APPEND_CHILD`, `MAKE_ERR`, `MAKE_LIT_*`, and a deterministic stack-template `MAKE_NODE` scaffold; bytecode operand-shape parity for `MAKE_NODE` remains pending. |

## Syscall ABI

| Area | Status | Notes |
|---|---|---|
| Typed syscall handler ABI | implemented | `AivmSyscallHandler` with target/typed args/result. |
| Contract table + type validation | implemented | Name/id lookup and deterministic arg/return checks. |
| UI draw syscall contract parity | in_progress | C contracts aligned for `ui_drawRect/ui_drawText/ui_drawLine/ui_drawEllipse/ui_drawPath/ui_drawImage` arities/types and canonical IDs. |
| UI lifecycle/window syscall parity | in_progress | Added C contracts for `ui_createWindow/ui_beginFrame/ui_endFrame/ui_pollEvent/ui_present/ui_closeWindow/ui_waitFrame`; `ui_getWindowSize` now matches C# shape (`1 int -> node`) and runtime dispatch tests enforce node returns for `ui_pollEvent/ui_getWindowSize`. |
| String syscall contracts (`sys.str_*`) | implemented | `utf8ByteCount`, `substring`, `remove` in C contract table and tests. |
| UI syscall contracts (initial set) | implemented | Contract scaffold supports UI targets used by VM tests. |
| Host mechanical boundary | implemented | VM does not call OS APIs directly. |

## Determinism Guardrails

| Guardrail | Status | Notes |
|---|---|---|
| No dynamic allocation in VM core | implemented | Fixed-capacity storage for loader/runtime/string arena. |
| No time/random/threading dependence | implemented | Core code path is pure state transition logic. |
| Replay stability checks | implemented | `aivm_test_vm_determinism` repeats mixed VM+syscall runs. |
| Warning-clean build enforcement | implemented | CMake applies `-Wall -Wextra -Wpedantic -Werror` and `/W4 /WX`. |
| Perf smoke baseline guard | implemented | `scripts/aivm-c-perf-smoke.sh` enforces median threshold from `AiVM.C/tests/perf_baseline.env` (optional via `AIVM_PERF_SMOKE=1`). |

## Parity/CI

| Area | Status | Notes |
|---|---|---|
| Dual-run parity compare utility | implemented | Normalized compare CLI + scripts wired in test flow. |
| Manifest parity runner | implemented | Per-case artifacts + exit-status parity checks, including optional asymmetric expected left/right status support. |
| `--vm=c` bridge gate parity | in_progress | Manifest includes deterministic `DEV008` gate case while runtime bridge remains backend-unlinked. |
| Multi-platform CI (macOS/Linux/Windows) | implemented | `aivm-c-ci` workflow builds/tests across matrix OSes. |
| Syscall-heavy golden parity suites | in_progress | Core `CALL_SYS` syscall-heavy C tests added; manifest now covers string + UI draw + UI lifecycle/window validation paths; expansion continues. |
