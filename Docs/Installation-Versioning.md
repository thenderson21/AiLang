# Installation and Versioning Contract

This document defines the long-term installation and versioning model for the
three public AiLangCore repositories.

## Goals

- Install a coherent AiLangCore toolchain from independently released
  repositories.
- Keep Git Flow release branches and tags as the source of release authority.
- Make component compatibility machine-readable without introducing JSON.
- Keep pre-`1.0.0` breaking changes explicit and traceable.

## Repositories

AiLangCore is released as three components:

- `AiLang`: compiler/toolset, stdlib, SDK docs, and `ailang` launcher.
- `AiVM`: native C VM, `aivm` executable, C library, and headers.
- `AiVectra`: vector UI library, UI SDK, and `aivectra` launcher.

Each repository owns its own source, CI, artifacts, and tags.

## Version Sources

Each repository MUST contain a root `VERSION` file with only the base semantic
version:

```text
0.0.1
```

The `VERSION` file does not include prerelease or build metadata. Release
automation derives prerelease identifiers from Git Flow branch context and CI
run identity.

Allowed derived versions:

- `0.0.1-alpha.N`
- `0.0.1-beta.N`
- `0.0.1-rc.N`
- `0.0.1`

Tags use `v` plus the derived version:

```text
v0.0.1-alpha.12
v0.0.1
```

## Git Flow Mapping

Branch roles:

- `develop`: integration for the next prerelease line.
- `feature/*`: scoped feature work branched from `develop`.
- `release/<version>`: stabilization for a specific base version.
- `hotfix/<version>`: urgent fixes from `main`.
- `main`: released commits only.

Version derivation:

- `develop` builds produce `X.Y.Z-alpha.N`.
- `release/X.Y.Z` builds produce `X.Y.Z-rc.N`.
- `main` tag builds produce `X.Y.Z`.
- `hotfix/X.Y.Z` builds produce `X.Y.Z-rc.N` until merged and tagged on
  `main`.

## Suite Version

The suite version is the version of the coordinated toolchain. During the early
release line, the suite version should match the AiLang base version.

Example:

```text
suite_version = "0.0.1"
```

The suite version does not require every component to have identical internal
implementation maturity, but the released component versions MUST be declared
compatible in the install manifest.

## Component Compatibility

Release artifacts MUST include an install manifest using TOML syntax.

Required shape:

```toml
suite_version = "0.0.1"
channel = "alpha"

[[component]]
name = "AiLang"
version = "0.0.1-alpha.12"
repo = "AiLangCore/AiLang"
artifact = "ailang-0.0.1-alpha.12-osx-arm64.tar.gz"

[[component]]
name = "AiVM"
version = "0.0.1-alpha.12"
repo = "AiLangCore/AiVM"
artifact = "aivm-0.0.1-alpha.12-osx-arm64.tar.gz"

[[component]]
name = "AiVectra"
version = "0.0.1-alpha.12"
repo = "AiLangCore/AiVectra"
artifact = "aivectra-0.0.1-alpha.12-osx-arm64.tar.gz"

[[requires]]
consumer = "AiLang"
provider = "AiVM"
range = ">=0.0.1-alpha.12 <0.0.2"
abi = "aivm-c-1"

[[requires]]
consumer = "AiVectra"
provider = "AiLang"
range = ">=0.0.1-alpha.12 <0.0.2"
```

No JSON install manifests are allowed.

## ABI Versioning

Semantic versions describe release compatibility. ABI versions describe binary
runtime compatibility.

Current ABI identifiers:

- AiBC format: `aibc=1`
- AiVM C ABI: `aivm-c-1`

Rules:

- Patch releases MUST NOT break ABI compatibility.
- Minor releases MAY introduce incompatible ABI changes before `1.0.0`.
- Any ABI break must update release notes, migration notes, and install
  manifest requirements.

## Installation Layout

The installed toolchain should use one root directory:

```text
AiLangCore/
+-- bin/
+-- lib/
+-- sdk/
+-- manifests/
`-- VERSION
```

Expected binaries:

```text
bin/ailang
bin/aivm
bin/aivectra
```

Expected metadata:

```text
manifests/install.toml
manifests/components.toml
```

## Installer Behavior

An installer MUST:

1. Resolve a suite version and target RID.
2. Download matching component artifacts.
3. Verify every artifact listed in the install manifest.
4. Check component semantic-version ranges.
5. Check required ABI identifiers.
6. Install into a single toolchain root.
7. Write the exact installed manifest.

Installer commands should eventually be:

```bash
ailang toolchain install 0.0.1-alpha.12 --target osx-arm64
ailang toolchain list
ailang toolchain use 0.0.1-alpha.12
ailang toolchain doctor
```

## Current Transitional Rules

- AiLang scripts may use a sibling `../AiVM/native` checkout during migration.
- Release artifacts should move toward consuming AiVM release artifacts instead
  of compiling AiVM source from AiLang.
- AiVectra should declare the AiLang and AiVM version/ABI requirements it was
  built and tested against.

## Immediate Implementation Tasks

1. Add root `VERSION` files to all three repositories.
2. Make release workflows derive versions from `VERSION`.
3. Add generated `install.toml` to release artifacts.
4. Add `--version` output that reports component version and ABI version.
5. Add installer validation before any global install command writes files.
