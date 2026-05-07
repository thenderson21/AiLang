# Three Repository Migration

This document defines the target public repository split for AiLangCore.

The current workspace has three checkouts:

- `AiLang`
- `AiVM`
- `AiVectra`

The migration goal is to make those repositories cleanly independent while
preserving a source-based development flow.

## Target Ownership

### AiLang

AiLang owns the language layer and SDK.

Contents:

- AiLang compiler/toolset written in AiLang (`src/compiler/*.aos`)
- AiLang core libraries (`src/std/*.aos`)
- language specs (`SPEC/`)
- SDK documentation and examples
- bootstrap wrappers only where required to execute the AiLang toolset

Deliverables:

- `ailang` executable or launcher
- AiLang core libraries
- AiLang SDK artifacts and docs

Long-term rule:

- AiLang should not own the C VM implementation.
- AiLang should not own AiVectra UI library implementation.
- AiLang command behavior should be rewritten in AiLang, not preserved as
  permanent C code elsewhere.

### AiVM

AiVM owns the virtual machine.

Contents:

- native C VM core
- AiBC program loader/runtime execution
- syscall dispatch boundary
- native tests and benchmarks
- VM release packaging

Deliverables:

- `aivm` executable
- embeddable native VM library and public C headers

Long-term rule:

- AiVM should be tiny, fast, deterministic, and host-effect-free except through
  explicit syscall dispatch.
- AiVM should interpret compiled AiLang programs, not own language/compiler
  semantics.
- Native launcher code in AiVM is a temporary bootstrap host until the AiLang
  CLI is self-hosted. See `Docs/AiLang-Self-Hosting-Rewrite.md`.

### AiVectra

AiVectra owns the UI library and SDK.

Contents:

- vector UI library code
- UI composition helpers
- AiVectra CLI/tooling
- UI specs, samples, and golden/debug fixtures

Deliverables:

- `aivectra` executable or launcher
- AiVectra libraries
- AiVectra SDK artifacts and samples

Long-term rule:

- AiVectra should consume AiLang and AiVM contracts, not define language
  semantics, VM scheduling, or generic runtime utilities.

## Current State

The native C VM has been imported into the standalone AiVM repository under:

```text
AiVM/native
```

The standalone AiVM repository now produces:

```text
aivm
libaivm_core.a
```

The old C# AiVM runtime has been archived in:

```text
AiVM/legacy/csharp/src/AiVM
```

AiLang no longer owns the pre-split native source path. Operational AiLang
scripts consume the sibling checkout at `../AiVM/native` by default, or the path
provided through `AIVM_C_SOURCE_DIR`.

## Migration Phases

### Phase 1: Document and freeze boundaries

- Keep dirty working changes intact.
- Do not move files until the source of truth is agreed.
- Update docs to name the C VM as the active VM.
- Make issue and task placement follow the target ownership above.

### Phase 2: Prepare AiVM native repository

- Done: imported `AiLang/src/AiVM.Core/native` into the `AiVM` repository with
  subtree history preserved.
- Done: added initial native `aivm` executable target.
- Done: archived the legacy C# tree under `legacy/csharp`.
- Pending: promote the native tree from `native/` to an AiVM-root layout:

```text
AiVM/
+-- include/
+-- src/
+-- tests/
+-- examples/
+-- CMakeLists.txt
+-- CMakePresets.json
+-- scripts/
`-- README.md
```

- Keep `aivm` as the primary executable deliverable.
- Keep the embeddable C library and headers as public VM artifacts.

### Phase 3: Rewire AiLang to consume AiVM

- Done: AiLang bootstrap scripts consume the sibling `../AiVM/native` checkout.
- Done: removed the tracked native VM implementation from AiLang.
- Pending: choose final dependency mechanism: submodule, sibling checkout, or
  release artifact.
- Run `./test.sh` in AiLang after the full removal step.

### Phase 4: Clean AiLang public surface

- Rename public launcher/tooling from legacy `airun` to the intended AiLang-facing
  `ailang`, while retaining temporary `airun` compatibility shims during alpha migration.
- Keep only compiler/toolset, stdlib, specs, SDK docs, and examples in AiLang.
- Remove obsolete VM ownership language from AiLang docs.

### Phase 5: Clean AiVectra public surface

- Keep AiVectra focused on vector UI library/tooling.
- Move generic runtime, syscall, parsing, HTTP, filesystem, task, and worker
  capabilities back to AiLang or AiVM.
- Keep samples deterministic and spec-governed.

## Immediate Safety Rules

- Do not rewrite or delete the standalone `AiVM` repository until its legacy C#
  contents have been intentionally retired or archived.
- Do not push subtree splits from a dirty source tree unless the missing dirty
  changes are understood.
- Do not introduce NuGet or external package dependencies as part of the split.
- Do not create duplicate syscall ownership across AiLang and AiVM.
