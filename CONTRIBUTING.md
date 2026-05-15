# Contributing to AiLang

AiLang owns the language, compiler/toolset, core libraries, SDK contracts, and
language specifications. Runtime execution belongs to AiVM. UI runtime work
belongs to AiVectra.

## Branches

AiLang uses Git Flow. The default integration branch is `develop`.

- Branch feature work from `develop`.
- Keep changes focused and reviewable.
- Do not add compatibility layers before the first major or minor release unless
  explicitly requested. Replace old contracts consistently when direction
  changes.

## Local Setup

For core development, clone the main repositories as siblings:

```bash
mkdir AiLangCore
cd AiLangCore
git clone https://github.com/AiLangCore/AiLang.git
git clone https://github.com/AiLangCore/AiVM.git
git clone https://github.com/AiLangCore/AiVectra.git
git clone https://github.com/AiLangCore/ailang-packages.git
git clone https://github.com/AiLangCore/ailang-core-packages.git
git clone https://github.com/AiLangCore/ailang-examples.git

git -C AiLang checkout develop
git -C AiVM checkout develop
git -C AiVectra checkout develop
```

Use the installed SDK for normal language work. Set `AIVM_C_SOURCE_DIR` only
when a task specifically needs AiVM source-level integration checks.

## Verification

Run from the AiLang repository root:

```bash
./test.sh
```

For package or SDK workflow changes, also validate with an installed SDK and the
curated examples repository:

```bash
cd ../ailang-examples
./scripts/validate-examples.sh
```

## Contribution Rules

- Keep `.aos` as the executable source format.
- Keep generated outputs out of commits: `.toolchain/`, `.tmp/`, `.artifacts/`,
  `app.aibc1`, local SDK files, and local notes.
- Do not add C VM/runtime/native launcher code to AiLang. That belongs in AiVM.
- Do not add syscalls for deterministic language or library behavior. Syscalls
  require host-boundary justification and AiVM contract updates.
- Semantic changes must update `SPEC/` and matching tests in the same change.
