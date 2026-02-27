# AiVM-C Replacement Tasks

## Scope

Replace the C# VM execution path with `AiVM.C` while preserving deterministic semantics and the existing high-level typed syscall ABI.

## Constraints

- No semantic changes to AiLang spec.
- No hidden side effects.
- No JSON in VM internals.
- No dynamic runtime dependencies.
- Keep host mechanical and syscall-boundary-only.

## Task List

1. Freeze VM contract and conformance checklist.
Status: `pending`
Output: explicit opcode/runtime/syscall parity matrix anchored to `SPEC/`.

2. Define C bytecode/program model (`AiBC1`) and deterministic loader.
Status: `completed`
Output: `aivm_program_*` API, deterministic load errors, no allocation in core.

3. Implement full VM runtime state model.
Status: `completed`
Output: stack, call frames, locals, constants, explicit halt/error state.

4. Port opcode execution semantics.
Status: `in_progress`
Output: switch-based deterministic dispatch with parity to current VM.

5. Complete C value model parity.
Status: `in_progress`
Output: all runtime value kinds and explicit ownership rules.

6. Port syscall dispatch/contracts in C.
Status: `in_progress`
Output: typed target/arg validation, stable IDs, deterministic diagnostics.

7. Runtime error and diagnostics parity.
Status: `in_progress`
Output: matching codes/messages/node-id behavior.

8. Build C bridge into CLI runtime selection.
Status: `in_progress`
Output: C ABI adapter with feature-flagged dual runtime path.

9. Add dual-run parity harness.
Status: `in_progress`
Output: C# VM vs C VM output/diagnostic diff checks across goldens.

10. Add syscall-heavy parity suites.
Status: `pending`
Output: lifecycle/http/publish/syscall scenarios parity gates.

11. Determinism + perf guardrails.
Status: `pending`
Output: replay-stability checks and non-regressive perf smoke baselines.

12. CI integration (multi-platform C build + parity jobs).
Status: `in_progress`
Output: required checks on macOS/Linux/Windows.

13. Cutover to C VM default.
Status: `pending`
Output: C VM default in CLI, temporary fallback flag for rollback window.

14. Remove deprecated C# VM path after soak.
Status: `pending`
Output: code cleanup and doc/runbook updates.

## Current Increment

- Completed deterministic AiBC1 instruction-section decode in `AiVM.C` with fixed-capacity storage and explicit decode errors.
- Added `AiVM.C` C-test harness (`ctest`) for program loader and VM core deterministic state transitions.
- Added initial `AivmValue` helper API (`void/int/bool/string` constructors and equality) with dedicated unit test.
- Added deterministic syscall dispatch-table skeleton and syscall unit test (`invoke` + `dispatch` paths).
- Added deterministic VM error code/message mapping scaffold with unit test coverage.
- Added deterministic VM stack primitives (push/pop, overflow/underflow errors) with unit test coverage.
- Added deterministic call-frame and locals primitives with overflow/underflow/bounds tests.
- Extended AiBC1 loader scaffold to deterministic header parsing (magic/version/flags) with unit tests.
- Added initial deterministic opcode execution primitives (`PUSH_INT`, `POP`, `STORE_LOCAL`, `LOAD_LOCAL`) with unit tests.
- Added parity normalization/equality helper module as groundwork for dual-run comparison tooling.
- Added AiBC1 section-table bounds validation in loader (truncated and out-of-bounds detection) with unit tests.
- Added `aivm_parity_cli` utility to support dual-run output comparison scripting.
- Added initial runtime bridge API (`aivm_execute_program`) plus unit test as CLI integration groundwork.
- Added deterministic `scripts/test-aivm-c.sh` test entrypoint for local/CI reuse.
- Added fixed-capacity section descriptor parsing in AiBC1 loader with section-limit and bounds validation tests.
- Added deterministic integer arithmetic opcode scaffold (`ADD_INT`) with type-safety error handling and tests.
- Added deterministic syscall-contract validation scaffold (`target + typed args + return kind`) with unit tests.
- Added `scripts/aivm-parity-compare.sh` and GitHub Actions workflow `aivm-c-ci.yml` for cross-platform build/test.
- Added C-ABI adapter scaffold (`aivm_c_api`) and test coverage for host integration entrypoints.
- Added deterministic control-flow opcode scaffolding (`JUMP`, `JUMP_IF_FALSE`) with bounds/type checks and tests.
- Added deterministic boolean literal opcode (`PUSH_BOOL`) and moved branch tests to opcode-driven setup.
- Added command-driven dual-run parity harness script (`scripts/aivm-dualrun-parity.sh`) for left/right command output comparison.
- Added deterministic call/return opcode scaffolding (`CALL`, `RET`) with frame-stack validation tests.
- Added deterministic integer equality opcode (`EQ_INT`) with type-safety tests.
- Added deterministic generic equality opcode (`EQ`) with value-equality and underflow tests.
- Fixed generic value equality string semantics to compare deterministic string content (not pointer identity), with VM/value tests.
- Hardened deterministic call-frame transitions: `RET` now restores frame base with single-value return semantics and rejects invalid negative control-flow/local operands.
- Added deterministic constant-pool loading (int/bool/string/void) in AiBC1 binary sections plus `CONST` opcode execution and tests.
- Expanded syscall contract handling with stable numeric IDs, deterministic target/id lookup, and typed validation tests.
- Added deterministic status-to-code/message mappings for AiBC1 loader and syscall-contract validation to support parity diagnostics.
- Extended C ABI runtime bridge with `aivm_c_execute_aibc1` to load+execute AiBC1 bytes and return deterministic loader/runtime status fields.
- Added manifest-driven dual-run parity harness (`scripts/aivm-dualrun-parity-manifest.sh`) and wired a parity smoke case into `scripts/test-aivm-c.sh` and CI path filters.
- Updated `aivm-c-ci` workflow with `workflow_dispatch` and parity-report artifact upload for deterministic parity debugging in CI.
- Added deterministic `STR_CONCAT` opcode support with fixed-capacity VM string arena (no heap allocation), plus opcode and diagnostics tests.
- Added deterministic `TO_STRING` opcode support for int/bool/string/null-like values using VM string arena storage, with VM opcode tests.
- Added contract-checked syscall dispatch path (`aivm_syscall_dispatch_checked`) enforcing deterministic arg/return type checks at runtime.
- Enhanced manifest parity runner to emit per-case output artifacts and deterministic report entries for CI/debug traceability.
- Added deterministic `STR_ESCAPE` opcode behavior (backslash/quote/newline/carriage-return/tab escaping) with strict type validation tests.
- Added explicit VM tests for fixed-capacity string arena overflow across `STR_CONCAT`, `TO_STRING`, and `STR_ESCAPE` (`AIVM009` behavior).
- Added deterministic syscall status code/message mapping (`AIVMS*`) for dispatch outcomes with unit coverage.
- Added explicit `RETURN` opcode alias support (mapped to `RET` semantics) to align C opcode surface with C# VM instruction naming.
- Expanded C syscall contract table with string operations (`sys.str_substring`, `sys.str_remove`) and added validation/id lookup tests.
- Expanded parity manifest smoke coverage to include CRLF/LF normalization and trailing-newline equivalence cases.
- Added rune-aware deterministic `STR_SUBSTRING` / `STR_REMOVE` VM opcodes (UTF-8 code-point clamped semantics) with unit tests.
- Added deterministic `CALL_SYS` opcode path with contract-checked host binding dispatch and explicit VM syscall failure diagnostic (`AIVM010`).
- Added explicit async/parallel opcode IDs (`ASYNC_CALL*`, `AWAIT`, `PAR_*`) with deterministic unsupported-op failure semantics in C core.
- Added `STR_UTF8_BYTE_COUNT` opcode support with type validation and UTF-8 byte-count test coverage.
- Extended runtime/C-API bridge with syscall-injected execution entrypoints (`aivm_execute_program_with_syscalls`, `aivm_c_execute_program_with_syscalls`) and tests.
- Added explicit node/attribute/child/make opcode IDs with deterministic placeholder failure semantics to align C decode/dispatch surface with C# VM opcode families.
- Updated CI parity artifacts to upload per-case left/right output files alongside parity reports for failure triage.
