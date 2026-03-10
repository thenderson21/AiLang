# AiVM Repository Split

This document defines the workflow for extracting `AiVM.Core` from this monorepo into `git@github.com:AiLangCore/AiVM.git` while preserving commit history.

## 1. Prepare split branch

From the monorepo root:

```bash
./scripts/split-aivm-repo.sh git@github.com:AiLangCore/AiVM.git main
```

This creates a subtree split from `src/AiVM.Core` and pushes it to `AiLangCore/AiVM`.

Options:

- `DRY_RUN=1 ./scripts/split-aivm-repo.sh` to validate split locally without pushing.
- `FORCE_PUSH=1 ./scripts/split-aivm-repo.sh` if the destination branch must be rewritten.
- `SPLIT_BRANCH=codex/aivm-split-<name> ./scripts/split-aivm-repo.sh` to control the temporary local branch name.

## 2. CI and release in the new repo

The split includes:

- `src/AiVM.Core/.github/workflows/ci.yml`
- `src/AiVM.Core/.github/workflows/release.yml`

After subtree split, those become:

- `.github/workflows/ci.yml`
- `.github/workflows/release.yml`

Behavior:

- `ci.yml` builds `AiVM.Core.csproj` on Linux, macOS, and Windows for push/PR.
- `release.yml` runs on tag push (`v*`), builds on Linux/macOS/Windows, creates platform-specific archives, and publishes a GitHub release with all artifacts attached.

## 3. Monorepo follow-up

After `AiVM` is split and owned by its own repository, wire it back into this repo as a submodule so project references remain source-based (no NuGet dependency introduced):

```bash
git submodule add git@github.com:AiLangCore/AiVM.git src/AiVM.Core
```

Then:

- remove tracked `src/AiVM.Core` files from the monorepo index (once submodule is added),
- keep existing `ProjectReference` paths (`..\AiVM.Core\AiVM.Core.csproj`) unchanged,
- run `./test.sh` to verify deterministic behavior and golden output.
