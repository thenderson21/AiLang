# Agent Code Map

## Core Layers

- `src/AiLang.Core`: language-facing placeholder/docs layer in this repo.
- `src/AiVM.Core`: deterministic VM/runtime layer.
  - native C VM sources live under `src/AiVM.Core/native/`
  - public headers live under `src/AiVM.Core/native/include/`
  - syscall implementations live under `src/AiVM.Core/native/sys/`
  - remote transport code lives under `src/AiVM.Core/native/remote/`
  - native tests live under `src/AiVM.Core/native/tests/`
- `src/AiCLI`: native CLI bootloader and host binding layer.
  - main entrypoint is `src/AiCLI/native/airun.c`
  - host adapters are split into `airun_*_host.inc` and platform UI host files
- `src/compiler`: AiLang-authored compiler/runtime scripts such as `aic.aos`, `format.aos`, and `validate.aos`
- `src/std`: stdlib AOS modules

## Primary Entry Points

- CLI entry: `src/AiCLI/native/airun.c`
- VM execution core: `src/AiVM.Core/native/aivm_vm.c`
- Program load/serialization: `src/AiVM.Core/native/aivm_program.c`
- Runtime host bridge: `src/AiVM.Core/native/aivm_runtime.c`
- Syscall contract logic: `src/AiVM.Core/native/sys/aivm_syscall_contracts.c`
- C API bridge: `src/AiVM.Core/native/aivm_c_api.c`

## Build And Test Surface

- Canonical bootstrap entrypoint: `./build.sh`
- Canonical verification entrypoint: `./test.sh`
- Native C test wrapper: `./test-aivm-c.sh`
- Native CMake config: `src/AiVM.Core/native/CMakeLists.txt`
- Native presets: `src/AiVM.Core/native/CMakePresets.json`

## Debug And Diagnostics

- Debug/bundle CLI flow: `src/AiCLI/native/airun.c`
- Debug host/bundle emission: `src/AiCLI/native/airun_debug_host.inc`
- Native parity/debug/memory tests: `src/AiVM.Core/native/tests/`
- Golden fixtures and publish fixtures: `examples/golden/`

## Samples

- CLI fetch sample: `samples/cli-fetch/project.aiproj`
- HTTP/weather API sample: `samples/weather-api/project.aiproj`
- Weather site sample: `samples/weather-site/project.aiproj`
- Parallel HTTP sample: `samples/cli-http-parallel/project.aiproj`

## Refactor Guardrails

- Keep VM as canonical execution path; AST is debug-only.
- Do not change semantics without matching `SPEC/` updates and fixture updates.
- Prefer `./build.sh` and `./test.sh` over ad hoc tool invocations for normal workflow.
