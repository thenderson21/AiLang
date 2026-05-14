# AiLang Self-Hosting Rewrite

Status: active migration plan.

## Goal

AiLang command-line tooling should be implemented in AiLang, not C.

Moving C out of the AiLang repository is only a boundary cleanup. It is not the
end state. The native C launcher currently living in AiVM is a bootstrap host
that should shrink over time until it only provides VM execution, SDK loading,
and host syscall adapters.

## Ownership

AiLang owns:

- compiler command behavior
- project initialization templates
- build, run, publish, test, and clean command policy
- diagnostics and user-facing command output
- SDK metadata and project-file interpretation

AiVM owns:

- native C VM execution
- AiBC loading
- host syscall dispatch
- native process/bootstrap entrypoint
- C library adapter boundary

Temporary exception:

- `tools/aos_frontend.c` remains in AiLang for now as the bootstrap AOS parser
  frontend. It should be rewritten in AiLang, but it is narrower than the native
  CLI launcher and is intentionally kept with the language/parser code during
  the rewrite.

## Current Starting Point

The existing AiLang-authored command surface starts in:

```text
src/compiler/aic.aos
```

The project manifest now points at that entrypoint:

```text
project.aiproj -> src/compiler/aic.aos / main
```

The new bytecode-oriented CLI entrypoint starts in:

```text
src/cli/ailang.aos
```

This is intentionally separate from `aic.aos`. The older compiler driver still
uses debug/compiler intrinsics that are not yet bytecode-lowerable. The new CLI
should stay runnable through production `aivm` from the start.

Current bootstrap status:

- `src/cli/ailang.aos` builds to `app.aibc1`.
- `aivm app.aibc1 --version` executes through the production VM.
- `init`, `template`, `agent`, `help`, `version`, and `project version` have
  first bytecode-runnable implementations.
- `init` renders installed `.tpl` files under `templates/projects`. Missing
  template files are an error.
- `build` has an alpha AiLang command implementation, but source-to-AiBC
  compilation still delegates to the bootstrap compiler process documented in
  `Docs/AiLang-Build.md`.
- `clean` is implemented directly in the AiLang-authored CLI.
- `run` has an alpha AiLang-authored command policy and delegates execution to
  `aivm` for bytecode or the bootstrap compiler for source/project inputs.
- `publish` has an alpha AiLang-authored command policy and delegates publish
  execution to the bootstrap compiler until the compiler runs as AiLang
  bytecode.
- Production `aivm` binds the process syscalls needed by general-purpose
  AiLang programs, so it can execute the alpha `build` path when the bootstrap
  compiler executable is present.
- Alpha command behavior is allowed to break until the first full release.
  Do not add compatibility aliases or alternate command paths yet.

The migrated C launcher is temporarily parked in:

```text
../AiVM/native/ailang_cli
```

## Rewrite Sequence

1. Keep `src/cli/ailang.aos` as the canonical bytecode-runnable AiLang CLI
   implementation target.
2. Move command policy from the native launcher into AiLang source:
   - `init`
   - `template`
   - `agent`
   - `clean`
   - `build`
   - `run`
   - `publish`
3. Keep native host operations behind explicit `sys.*` calls.
4. Add bytecode CLI tests for the canonical alpha command behavior.
5. Change SDK packaging so `ailang` launches compiled AiLang CLI bytecode.
6. Delete native command-policy code from AiVM after the bytecode CLI owns the
   command surface.

## Non-Goals

- Do not preserve C command logic as a permanent implementation.
- Do not move language semantics, compiler policy, or project behavior into
  AiVM.
- Do not add C back to AiLang for bootstrap convenience.

## Release Rule

Alpha releases may still ship the temporary native launcher, but release notes
must describe it as a bootstrap implementation. The sponsorship-ready direction
is self-hosted AiLang tooling with AiVM as the small C execution engine.
