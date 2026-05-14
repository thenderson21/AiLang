# AiLang Clean Command

Status: alpha command contract.

## Command

```bash
ailang clean <project-dir>
```

`<project-dir>` must contain `project.aiproj`.

`clean` removes generated project output:

- `<project-dir>/bin`
- `<project-dir>/dist`
- `<project-dir>/.toolchain`

Missing generated directories are ignored.

## Current Implementation

`src/cli/ailang.aos` owns and implements the command directly with filesystem
syscalls. No bootstrap compiler delegation is required.

## Errors

- Missing path returns `AILANG001`.
- Missing `project.aiproj` returns `AILANG007`.
