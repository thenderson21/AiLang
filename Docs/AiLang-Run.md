# AiLang Run Command

Status: alpha bootstrap contract.

## Command

```bash
ailang run <project-dir|source.aos|program.aibc1> [args...]
```

`ailang run` validates that the path exists, then delegates execution during
the alpha bootstrap phase:

- `.aibc1` bytecode runs through `aivm`.
- project/source inputs run through the selected bootstrap `ailang`.

Set `AIVM=/path/to/aivm` to override the VM used for bytecode execution.

## Current Implementation

`src/cli/ailang.aos` owns the command contract, path validation, app argument
forwarding, VM selection for bytecode, and bootstrap compiler selection for
source/project execution.

Child process exit codes are propagated. Child stdout/stderr forwarding depends
on the active `sys.process.*` host implementation during the alpha bootstrap
phase.

## Errors

- Missing path returns `AILANG001`.
- Nonexistent run path returns `AILANG011`.
