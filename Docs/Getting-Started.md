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
4. Run compiler driver modes:
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

## Failure Codes

- `airun run`: `0` success, `2` parse/validation error, `3` runtime error.
- `scripts/test.sh`: nonzero if any golden fails.
- Update-path blocking guard:
  - `VAL340` at validation-time
  - `RUN031` at runtime/VM-time

## Debugging Notes

- For golden verification, prefer native `airun` execution over `dotnet .../airun.dll` because the golden harness spawns subprocesses using the current process path.
- If `./tools/airun` is not usable in your environment, use the published native artifact:
```bash
./.artifacts/airun-osx-arm64/airun run --vm=ast src/compiler/aic.aos test examples/golden
```

## Minimal Import Example

```aos
Program#p1 {
  Import#i1(path="./src/std/str.aos")
  Call#c1(target=concat) { Lit#l1(value="a") Lit#l2(value="b") }
}
```
