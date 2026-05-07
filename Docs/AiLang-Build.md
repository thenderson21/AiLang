# AiLang Build Command

Status: alpha bootstrap contract.

## Command

```bash
ailang build <project-dir> [--out <dir>]
```

`<project-dir>` must contain `project.aiproj`.

Output is always:

```text
<out-dir>/app.aibc1
```

If `--out` is omitted, the output directory is:

```text
<project-dir>/bin
```

## Current Implementation

`src/cli/ailang.aos` owns the command contract, argument validation, output
path policy, and user-facing diagnostics.

During the alpha bootstrap phase it delegates source-to-AiBC compilation to a
bootstrap compiler process:

```text
AILANG_BOOTSTRAP_COMPILER, when set
./tools/airun, otherwise
```

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
- Bootstrap compiler failure returns `AILANG014`.
