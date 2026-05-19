# AiLang Test Workflow

Status: beta temporary contract.

AiLang does not currently expose a project-level `ailang test` command. For the
beta line, testing is defined by repository and example validation entrypoints.
Do not document or depend on `ailang test` until the command is implemented.

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

If a project has its own test script, that script owns project-specific test
policy. The SDK only guarantees restore/build/run until `ailang test` is
implemented.

## Future `ailang test`

The eventual `ailang test` command should:

- run project-local tests without requiring repository scripts
- consume restored packages from `ailang.lock.toml`
- avoid implicit network fetches
- return deterministic exit codes and diagnostics
- support CI-friendly output
- stay separate from VM/runtime conformance tests owned by AiVM
