# Agent Code Map

## Core Layers

- `src/AiLang.Core`: language-facing placeholder/docs layer in this repo.
- `../AiVM/native`: deterministic VM/runtime layer owned by the AiVM repository.
  - native C VM sources live under `../AiVM/native/`
  - public headers live under `../AiVM/native/include/`
  - syscall implementations live under `../AiVM/native/sys/`
  - remote transport code lives under `../AiVM/native/remote/`
  - native launcher/host adapter code lives under `../AiVM/native/ailang_cli/`
  - native tests live under `../AiVM/native/tests/`
- `src/compiler`: AiLang-authored compiler/runtime scripts such as `aic.aos`, `format.aos`, and `validate.aos`
- `src/std`: stdlib AOS modules

## Primary Entry Points

- Native launcher entry: `../AiVM/native/ailang_cli/airun.c`
- VM execution core: `../AiVM/native/aivm_vm.c`
- Program load/serialization: `../AiVM/native/aivm_program.c`
- Runtime host bridge: `../AiVM/native/aivm_runtime.c`
- Syscall contract logic: `../AiVM/native/sys/aivm_syscall_contracts.c`
- C API bridge: `../AiVM/native/aivm_c_api.c`

## Build And Test Surface

- Canonical bootstrap entrypoint: `./build.sh`
- Canonical verification entrypoint: `./test.sh`
- Native C test wrapper: `./test-aivm-c.sh`
- Native CMake config: `../AiVM/native/CMakeLists.txt`
- Native presets: `../AiVM/native/CMakePresets.json`

## Debug And Diagnostics

- Debug/bundle CLI flow: `../AiVM/native/ailang_cli/airun.c`
- Debug host/bundle emission: `../AiVM/native/ailang_cli/airun_debug_host.inc`
- Native parity/debug/memory tests: `../AiVM/native/tests/`
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
