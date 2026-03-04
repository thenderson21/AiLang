# Getting Started (Agent Procedure)

## Purpose

Run build and verification flows with deterministic outputs.

## Preconditions

- Execute from repo root.
- Shell has execute permission for scripts.

## Procedure

1. Build runtime:
```bash
./scripts/build-airun.sh
```
2. Execute golden suite:
```bash
./scripts/test.sh
```
3. Run a program:
```bash
./tools/airun run examples/hello.aos
```
4. Build bytecode from project/source:
```bash
./tools/airun build samples/cli-fetch/project.aiproj --out ./.tmp/build-cli-fetch
```
5. Publish wasm package (web default):
```bash
./tools/airun publish samples/cli-fetch/project.aiproj --target wasm32 --out ./.tmp/publish-cli-fetch-wasm
```
6. Run compiler driver modes:
```bash
cat examples/golden/fmt_basic.in.aos | ./tools/airun run --vm=ast src/compiler/aic.aos fmt
cat examples/golden/fmt_basic.in.aos | ./tools/airun run --vm=ast src/compiler/aic.aos fmt --ids
cat examples/golden/check_missing_name.in.aos | ./tools/airun run --vm=ast src/compiler/aic.aos check
cat examples/golden/run_var.in.aos | ./tools/airun run --vm=ast src/compiler/aic.aos run
```

## Expected Output

- `build-airun.sh`: rebuilds `tools/airun`.
- `test.sh`: prints `PASS/FAIL` per golden and final `Ok#ok1(type=int value=0)` on success.
- `aic fmt/check/run`: emits canonical AOS only.
- Source node ids are optional; canonical ids are assigned deterministically.
- `publish --target wasm32`:
  - default profile `spa` writes `index.html` + `main.js`.
  - `--wasm-profile cli` writes `run.sh` + `run.ps1`.
  - `--wasm-profile fullstack` writes a root app binary and `www/` wasm web artifacts.
    - also emits a self-contained root app binary (`./<appname>` or `.<\\appname>.exe`) for published-package execution.
    - optional override: `--wasm-fullstack-host-target <rid>`
    - project manifest override: `publishWasmFullstackHostTarget="<rid>"`
    - running the root app binary serves `www/` at `http://localhost:8080` (override with `PORT`).
  - malformed bytecode/source inputs are rejected deterministically with `DEV008` at publish time.

## Failure Codes

- `airun run`: `0` success, `2` parse/validation error, `3` runtime error.
- `scripts/test.sh`: nonzero if any golden fails.
- Update-path blocking guard:
  - `VAL340` at validation-time
  - `RUN031` at runtime/VM-time

## Debugging Notes

- For golden verification, prefer native `airun` execution over `dotnet .../airun.dll` because the golden harness spawns subprocesses using the current process path.
- Native toolchain is C-only in active workflows; no C#/DLL fallback is required for command execution.
- If `./tools/airun` is not usable in your environment, use the published native artifact:
```bash
./.artifacts/airun-osx-arm64/airun run --vm=ast src/compiler/aic.aos test examples/golden
```
- For wasm publish/run validation, build the wasm runtime artifact first:
```bash
./scripts/build-aivm-wasm.sh
```

## Current Compile Coverage Note

- `build/run/publish` share one native compile pipeline.
- Imports-heavy project shapes may still return deterministic `DEV008` until native compile coverage completes for those constructs.

## Minimal Import Example

```aos
Program#p1 {
  Import#i1(path="./src/std/str.aos")
  Call#c1(target=concat) { Lit#l1(value="a") Lit#l2(value="b") }
}
```
