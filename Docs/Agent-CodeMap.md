# Agent Code Map

## Core Layers

- `src/AiLang.Core`: language layer (AST, parser frontend bridge, validator, formatter host wiring, AOS runtime semantics).
- `src/AiVM.Core`: VM/runtime host layer (AiBC1 load/run, syscall host wrappers, bundle loader/publisher, process/file/network adapters).
- `src/AiCLI`: CLI bootloader (`run`, `serve`, `repl`, `bench`) and process entry wiring.
- `src/AiVectra`: UI layer placeholder project (no runtime integration yet).
  - reserved UI spec path: `src/AiVectra/SPEC/UI.md`
- `src/compiler`: AiLang-authored compiler/runtime scripts (`aic.aos`, `runtime.aos`, `format.aos`, `validate.aos`, `route.aos`, `json.aos`, `http.aos`).

## Primary Entry Points

- CLI entry: `src/AiCLI/Program.cs`
- Interpreter entry: `src/AiLang.Core/AosInterpreter.cs`
  - Core eval dispatch: `src/AiLang.Core/AosInterpreter.CoreEval.cs`
  - Eval loop + trace: `src/AiLang.Core/AosInterpreter.EvalLoop.cs`, `src/AiLang.Core/AosInterpreter.Trace.cs`
  - Call dispatch: `src/AiLang.Core/AosInterpreter.Calls.cs`
  - VM call entry + bridges: `src/AiLang.Core/AosInterpreter.VmEntry.cs`, `src/AiLang.Core/AosInterpreter.VmRunCall.cs`
  - Compiler calls: `src/AiLang.Core/AosInterpreter.CompilerCalls.cs`
  - Sys/capability bridge: `src/AiLang.Core/AosInterpreter.SysBridge.cs`
  - Import/publish helpers: `src/AiLang.Core/AosInterpreter.Imports.cs`, `src/AiLang.Core/AosInterpreter.Publish.cs`
- VM entry: `src/AiVM.Core/VmEngine.cs`
- Golden harness: `src/AiLang.Core/AosInterpreter.Golden.cs`

## Shared Utilities

- Compiler asset discovery and load: `src/AiLang.Core/AosCompilerAssets.cs`
- Host wrappers (deterministic indirection): `src/AiVM.Core/Host*.cs`

## Test Surface

- Unit/integration tests: `tests/AiLang.Tests/AosTests.cs`
- Golden fixtures: `examples/golden/**/*.in.aos`, `.out.aos`, `.err`
- Golden runner: `./scripts/test.sh`

## Debug Playbook

- Fast local signal:
  - `./scripts/test.sh`
  - expected success tail: `Ok#ok1(type=int value=0)`
- Focused guard tests:
  - `dotnet test tests/AiLang.Tests/AiLang.Tests.csproj -c Release --no-restore --filter "Validator_UpdateContext_RejectsBlockingCalls|VmRunBytecode_UpdateContext_RejectsBlockingCall_Transitively|AstRuntime_UpdateContext_RejectsBlockingCall_Transitively"`
- Blocking-guard signatures:
  - validation-time: `VAL340` (`Blocking call '...' is not allowed in update context.`)
  - runtime/VM-time: `RUN031` (`Blocking call '...' is not allowed during update.`)
- Golden harness caveat:
  - Goldens in `AosInterpreter.Golden` spawn the host binary via `Environment.ProcessPath`.
  - Running the harness through `dotnet .../airun.dll` can produce false failures because subprocesses resolve to `dotnet`.
  - Prefer native host invocation for golden runs:
    - `./tools/airun run --vm=ast src/compiler/aic.aos test examples/golden`
  - If `./tools/airun` path behaves unexpectedly in your environment, run the published artifact directly:
    - `./.artifacts/airun-osx-arm64/airun run --vm=ast src/compiler/aic.aos test examples/golden`

## Refactor Guardrails

- Do not change semantics without updating:
  - `SPEC/IL.md`
  - `SPEC/EVAL.md`
  - `SPEC/VALIDATION.md`
  - matching golden fixtures
- Keep VM as canonical path. AST is debug-only (`--vm=ast`).
- Keep outputs canonical AOS and deterministic.
