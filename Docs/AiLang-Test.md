# AiLang Test Workflow

Status: beta contract.

AiLang exposes a project-level `ailang test` command for app projects. The
command is intentionally small for beta: it discovers project-local tests,
runs them through the normal build/run path, and returns deterministic process
status.

## AiLang Repository

From the AiLang repository root:

```bash
./test.sh
```

This is the canonical AiLang verification gate. It builds the native toolchain
as needed and runs the deterministic compiler, stdlib, debug, package, and
runtime coverage currently owned by AiLang.

## AiVM Repository

From the AiVM repository root:

```bash
./test-aivm-c.sh
```

This is the canonical native VM verification gate. It builds and runs the C VM
test suite across runtime, syscall, memory, remote, package-manager bridge, and
determinism coverage.

## AiVectra Repository

From the AiVectra repository root:

```bash
./scripts/test-all.sh
```

This is the canonical AiVectra verification gate. Visual/runtime tests that
need a display or screenshot capture may require host-specific setup documented
in AiVectra.

## Examples Repository

From the `ailang-examples` repository root with an SDK on `PATH`:

```bash
./scripts/validate-examples.sh
```

This verifies the public examples, including the package-backed demo.

## App Projects

For beta app projects, the required public smoke is:

```bash
ailang package restore .
ailang build .
ailang run .
```

Run project-local tests:

```bash
ailang test .
```

`ailang test [project-dir] [--no-cache]` discovers tests in deterministic
order:

- `tests/project.aiproj`, if present
- direct `tests/*.aos` files, sorted lexically, if no nested project exists
- `Tests/project.aiproj`, if present and distinct from `tests/`
- direct `Tests/*.aos` files, sorted lexically, if no nested project exists

Each discovered test is built and run through the same native AiBC1 path used
by `ailang run`. Test stdout/stderr is preserved. If no tests exist, the
command succeeds with `Ok#ok1(type=int value=0)`.

## Future Hardening

Future `ailang test` hardening should:

- support test filters
- add machine-readable test result output
- add package test conventions
- avoid implicit network fetches
- keep deterministic exit codes and diagnostics
- stay separate from VM/runtime conformance tests owned by AiVM
