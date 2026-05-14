# Self-Hosting Checklist

Status: active migration checklist.

Self-hosted AiLang means the normal `ailang build` pipeline is implemented in
AiLang and runs on AiVM. The native/bootstrap compiler may still be used to
build the first self-hosted compiler, but it must not remain the normal
compiler path.

## Execution Order

1. Package manager
   - `ailang package list`
   - `ailang package restore`
   - `ailang.lock.toml`
   - local package cache
   - package imports resolvable by compiler
   - package tool conflict checks
   - package templates discoverable by `ailang template`
   - temporary C bridge wrappers isolated behind the AiVM native bridge when
     the AiLang implementation is not available yet

2. Parser in AiLang
   - parse `.aos` into canonical IL nodes
   - deterministic IDs
   - deterministic diagnostics

3. Validator in AiLang
   - enforce IL contracts
   - enforce project/package manifest contracts
   - enforce syscall discipline

4. Resolver in AiLang
   - relative imports
   - package imports through lockfile/cache
   - circular import diagnostics
   - stable module graph

5. Linker in AiLang
   - start from `Project.entryFile` and `Project.entryExport`
   - include only reachable modules, functions, constants, and declared assets
   - exclude unused package source, tests, examples, tools, templates, and docs
   - emit link report

6. Bytecode emitter in AiLang
   - deterministic AiBC/AiBE output
   - stable constants and instruction order
   - source/debug metadata where requested

7. CLI in AiLang
   - `init`
   - `template`
   - `package`
   - `build`
   - `run`
   - `publish`
   - package tool dispatch

8. Bootstrap handoff
   - build current compiler with bootstrap path
   - build compiler with self-hosted compiler
   - compare deterministic outputs where possible
   - switch normal `ailang build` to self-hosted compiler
   - leave native/compiler fallback out of normal paths

## Current First Milestone

The current milestone is package manager + package import resolution because
every later self-hosted phase needs the same project graph:

```bash
ailang package list
ailang package restore
ailang build .
```

The package manager must support three package item types:

- `library`: importable AiLang source.
- `tool`: executable command or project tool.
- `template`: project or file template surfaced by `ailang template`.

If a package tool name conflicts with an existing compiled command, globally
installed tool, or locally installed package tool, restore/install must fail.
