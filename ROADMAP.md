# AiLangCore Roadmap

Status: public planning roadmap for AiLang, AiVM, and AiVectra.

AiLangCore is an AI-first programming platform built around deterministic
language semantics, a tiny native VM, and a vector UI/runtime SDK.

## Architecture Direction

- AiLang owns language semantics, compiler/tooling, core libraries, package
  restore, project templates, and SDK layout.
- AiVM owns the native C runtime, bytecode execution, syscall dispatch, memory
  management, diagnostics, and embeddable VM library.
- AiVectra owns the vector UI SDK, scene graph semantics, app runtime
  integration, and UI samples.

The public runtime direction is the native C AiVM. Legacy runtime paths are
bootstrap or archive material only unless explicitly marked otherwise.

## Alpha

Goal: make the project installable, understandable, and demoable.

Current priorities:

- Keep public docs aligned with the three-repo architecture.
- Keep `develop` branches buildable and testable.
- Ensure install scripts install the current SDK and native VM artifacts.
- Keep `ailang init`, `ailang build`, `ailang run`, and `ailang publish`
  coherent.
- Prove package restore with a curated git-backed registry.
- Keep AiVM memory behavior deterministic and covered by regression tests.
- Keep at least one AiVectra app/sample runnable for demos.

Alpha is allowed to change contracts freely. Do not add compatibility layers
unless explicitly requested.

## Beta

Goal: make the project credible for outside contributors, conference demos, and
sponsor review.

Beta gates are tracked in:

```text
BETA_READINESS.md
```

Minimum beta outcomes:

- install flow works on macOS, Linux, and Windows
- native AiVM artifacts are released for supported hosts
- `ailang init/build/run` works from installed SDKs
- package restore works and is documented
- public specs have clear ownership
- resource limits and error codes are stable enough for beta
- at least one AiVectra sample app is functional and documented
- website, releases, README files, and branch story are consistent

## Release Candidate

Goal: lock behavior for the first stable release.

RC requirements:

- semantic specs are internally consistent
- CLI contracts are frozen for 1.0
- package registry format is frozen for 1.0
- release automation is repeatable
- install/update flow is repeatable
- cross-platform CI is green
- docs contain no known architecture contradictions

## 1.0

Goal: a stable first release for real users and contributors.

1.0 requirements:

- deterministic language contracts are stable
- native AiVM is the default runtime
- SDK layout and version selection are stable
- package restore is stable
- AiVectra has a documented app model
- public docs explain how humans and agents start projects
- sponsorship and contribution paths are clear

## Long-Term Direction

- Self-host the AiLang compiler/tooling in AiLang.
- Keep AiVM tiny, fast, deterministic, and embeddable.
- Preserve AOT/JIT feasibility by keeping bytecode and host effects explicit.
- Support platform-preferred publish layouts.
- Support WebAssembly without changing language semantics.
- Grow AiVectra into a deterministic vector UI/runtime SDK.
