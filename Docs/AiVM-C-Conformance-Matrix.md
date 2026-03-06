# AiVM-C Conformance Matrix

## Scope

This matrix tracks native `src/AiVM.Core/native` parity work against the current AiLang contracts in:

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
| VM error detail strings | implemented | Deterministic detail channel (`aivm_vm_error_detail`) is in place across core opcode/runtime failure paths, including contract-subcoded syscall details from `CALL_SYS` (`AIVMS004/AIVMC*`) and opcode-specific node/attr diagnostics. |
| Runtime bridge API | implemented | `aivm_execute_program*` and C ABI adapters are present as deterministic host-boundary APIs. |

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
| `ASYNC_CALL*`, `AWAIT`, `PAR_*` | implemented | Deterministic semantics implemented for `ASYNC_CALL`, `ASYNC_CALL_SYS`, `AWAIT`, and `PAR_BEGIN/FORK/JOIN/CANCEL`; `PAR_JOIN` now materializes a deterministic `Block` node with resolved child values (including completed task-handle resolution) to align runtime behavior with canonical VM structure. |
| `NODE_*`, `ATTR_*`, `CHILD_*`, `MAKE_*` | implemented | Deterministic `NODE_*`, `ATTR_*`, `CHILD_*`, `MAKE_BLOCK`, `APPEND_CHILD`, `MAKE_ERR`, `MAKE_LIT_*`, `MAKE_FIELD_STRING`, `MAKE_MAP`, and stack-template `MAKE_NODE` semantics are implemented in the C runtime. |

## Syscall ABI

| Area | Status | Notes |
|---|---|---|
| Typed syscall handler ABI | implemented | `AivmSyscallHandler` with target/typed args/result. |
| Contract table + type validation | implemented | Name/id lookup and deterministic arg/return checks. |
| Canonical syscall ID coverage | implemented | C contract table now covers full `SyscallId` range (`0..89`) without missing/duplicate IDs. |
| UI draw syscall contract parity | in_progress | C contracts aligned for `ui_drawRect/ui_drawText/ui_drawLine/ui_drawEllipse/ui_drawPath/ui_drawImage` arities/types and canonical IDs. |
| UI lifecycle/window syscall parity | in_progress | Added C contracts for `ui_createWindow/ui_beginFrame/ui_endFrame/ui_pollEvent/ui_present/ui_closeWindow/ui_waitFrame`; `ui_getWindowSize` now matches C# shape (`1 int -> node`) and runtime dispatch tests enforce node returns for `ui_pollEvent/ui_getWindowSize`. |
| String syscall contracts (`sys.str_*`) | implemented | `utf8ByteCount`, `substring`, `remove` in C contract table and tests. |
| Console syscall contracts (`sys.console_*`, `sys.stdout_*`) | in_progress | Added core write/read/writeErr/stdout contracts with canonical IDs and typed dispatch coverage. |
| Process/runtime metadata syscall contracts | in_progress | Added `sys.process.cwd/sys.process.env.get/sys.process.args/sys.platform/sys.arch/sys.os.version/sys.runtime` with canonical IDs and return-kind coverage. |
| Time/filesystem syscall contracts | in_progress | Added `sys.time.nowUnixMs/sys.time.monotonicMs/sys.time.sleepMs/sys.process.exit` and `sys.fs_*` contracts with canonical IDs plus representative typed dispatch tests. |
| Crypto syscall contracts | in_progress | Added `sys.crypto.base64Encode/sys.crypto.base64Decode/sys.crypto.sha1/sys.crypto.sha256/sys.crypto.hmacSha256/sys.crypto.randomBytes` with canonical IDs and typed coverage. |
| Network syscall contracts | in_progress | Added `sys.net_*` legacy + `sys.net.tcp*` + `sys.net.udp*` + `sys.net.async*` contract entries with canonical IDs and typed validation coverage. |
| Worker/debug syscall contracts | in_progress | Added `sys.worker_*` and `sys.debug_*` contract entries with canonical IDs and typed validation coverage. |
| UI syscall contracts (initial set) | implemented | Contract scaffold supports UI targets used by VM tests. |
| Host mechanical boundary | implemented | VM does not call OS APIs directly. |

## Determinism Guardrails

| Guardrail | Status | Notes |
|---|---|---|
| No dynamic allocation in VM core | implemented | Fixed-capacity storage for loader/runtime/string arena. |
| No time/random/threading dependence | implemented | Core code path is pure state transition logic. |
| Replay stability checks | implemented | `aivm_test_vm_determinism` repeats mixed VM+syscall runs. |
| Warning-clean build enforcement | implemented | CMake applies `-Wall -Wextra -Wpedantic -Werror` and `/W4 /WX`. |
| Perf smoke baseline guard | implemented | `scripts/aivm-c-perf-smoke.sh` enforces median threshold from `src/AiVM.Core/native/tests/perf_baseline.env` (optional via `AIVM_PERF_SMOKE=1`). |

## Parity/CI

| Area | Status | Notes |
|---|---|---|
| Dual-run parity compare utility | implemented | Normalized compare CLI + scripts wired in test flow. |
| Manifest parity runner | implemented | Per-case artifacts + exit-status parity checks, including optional asymmetric expected left/right status support. |
| `--vm=c` runtime entrypoint parity | implemented | Dashboard strict checks pass for run-source/bytecode/bundle. `serve` is intentionally out-of-scope for native runtime surface and enforced as non-goal. |
| Multi-platform CI (macOS/Linux/Windows) | implemented | `aivm-c-ci` workflow builds/tests across matrix OSes. |
| Syscall-heavy golden parity suites | implemented | Core `CALL_SYS` syscall-heavy C tests added; manifest covers string/UI/net/crypto/worker validation paths with deterministic expected output/status checks. |
| Memory leak threshold gate (matrix CI) | implemented | `scripts/aivm-mem-audit.sh`/`.ps1` enforce RSS growth threshold on Linux/macOS/Windows and emit deterministic TOML artifacts in `aivm-c-ci`. |
