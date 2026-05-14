# AiLang Build Command

Status: alpha bootstrap contract.

## Command

```bash
ailang build <project-dir> [--out <dir>]
```

`<project-dir>` must contain `project.aiproj`. The manifest must declare a
non-empty `entryFile` and `entryExport`, and `entryFile` must point at an
existing source file under the project directory.

Output is always:

```text
<out-dir>/app.aibc1
```

If `--out` is omitted, the output directory is:

```text
<project-dir>/bin
```

Build reads restored dependencies from `ailang.lock.toml` and the local package
cache. It must not fetch package repositories implicitly. If a package required
by the manifest or import graph is missing from the lockfile/cache, build fails
with a deterministic diagnostic and the caller should run:

```bash
ailang package restore
```

Compiled output is reachable-code only. Starting from `Project.entryFile` and
`Project.entryExport`, the compiler emits only modules, functions, constants,
and data reachable through explicit imports and calls. Restored package source,
unused package exports, docs, tests, examples, and unrelated assets are not part
of the output.

## Current Implementation

`src/cli/ailang.aos` owns the command contract, argument validation, output
path policy, and user-facing diagnostics.

During the alpha bootstrap phase it delegates source-to-AiBC compilation to a
bootstrap compiler process:

```text
AILANG_BOOTSTRAP_COMPILER, when set
project ailang-toolchain.toml selected SDK, when set
./tools/ailang, when running from an AiLang source checkout
~/.ailang/current/bin/ailang, when installed
ailang, otherwise
```

`AILANG_TOOLCHAIN` overrides the project-local selector for this resolution
step. `AILANG_INSTALL_ROOT` overrides the SDK root; otherwise the root is
`~/.ailang`. The special selector `local` resolves to
`~/.ailang/local/bin/ailang`; other selectors resolve to
`~/.ailang/toolchains/<version>/bin/ailang`.

Production `aivm` binds the process syscalls needed to run this alpha build
path, so the compiled AiLang CLI can execute `build` under `aivm` as long as the
bootstrap compiler executable is present.

This is temporary. The long-term implementation must replace the bootstrap
compiler process with AiLang-authored compiler code that runs as compiled
AiLang bytecode. Do not move compiler semantics into AiVM syscalls to close this
gap.

## Errors

- Missing path returns `AILANG001`.
- Missing `project.aiproj` returns `AILANG007`.
- Missing or empty `entryFile` returns `AILANG008`.
- Missing or empty `entryExport` returns `AILANG009`.
- Missing entry source file returns `AILANG010`.
- Bootstrap compiler failure returns `AILANG014`.
