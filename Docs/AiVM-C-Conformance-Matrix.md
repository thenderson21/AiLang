# AiVM-C Conformance Matrix

## Scope

This matrix tracks `AiVM.C` parity work against the current AiLang contracts in:

- `SPEC/BYTECODE.md`
- `SPEC/IL.md`
- `SPEC/EVAL.md`
- `SPEC/VALIDATION.md`

Status keys:

- `implemented`: deterministic behavior exists in C runtime
- `placeholder`: opcode/feature is explicitly rejected deterministically
- `pending`: not yet ported into C runtime surface

## Runtime Core

| Area | Status | Notes |
|---|---|---|
| AiBC1 binary header/section decode | implemented | Deterministic loader checks with explicit status mapping. |
| VM state container (stack/frames/locals/ip) | implemented | Explicit mutable state, no globals, no hidden effects. |
| Deterministic step/run loop | implemented | Switch-based dispatch, no reflection/computed goto. |
| VM diagnostics mapping | implemented | Stable deterministic code/message mapping in C layer. |
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
| `ASYNC_CALL*`, `AWAIT`, `PAR_*` | placeholder | Deterministic unsupported-op failure path. |
| `NODE_*`, `ATTR_*`, `CHILD_*`, `MAKE_*` | placeholder | Deterministic unsupported-op failure path. |

## Syscall ABI

| Area | Status | Notes |
|---|---|---|
| Typed syscall handler ABI | implemented | `AivmSyscallHandler` with target/typed args/result. |
| Contract table + type validation | implemented | Name/id lookup and deterministic arg/return checks. |
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

## Parity/CI

| Area | Status | Notes |
|---|---|---|
| Dual-run parity compare utility | implemented | Normalized compare CLI + scripts wired in test flow. |
| Manifest parity runner | implemented | Per-case artifacts + exit-status parity checks. |
| Multi-platform CI (macOS/Linux/Windows) | implemented | `aivm-c-ci` workflow builds/tests across matrix OSes. |
| Syscall-heavy golden parity suites | in_progress | Core `CALL_SYS` syscall-heavy C tests added; golden manifest expansion continues. |
