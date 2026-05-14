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

AiLang and AiVectra are AiLang projects, so their base semantic versions MUST
come from their root `project.aiproj` files:

```aos
Program#p1 {
  Project#proj1(name="AiLang" entryFile="src/main.aos" entryExport="main" version="0.0.1")
}
```

AiVM is a native C project and does not have an AiLang project manifest. AiVM
therefore uses the CMake project declaration in `native/CMakeLists.txt` as the
base semantic version:

```cmake
project(aivm_c_core VERSION 0.0.1 LANGUAGES C)
```

The base version source does not include prerelease or build metadata. Release
automation derives prerelease identifiers from Git Flow branch context and CI
run identity.

Allowed derived versions:

- `0.0.1-alpha.N`
- `0.0.1-beta.N`
- `0.0.1-rc.N`
- `0.0.1-local` for local-only development builds
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

Released toolchains should install under the user installation root:

```text
~/.ailang/
+-- bin/
+-- current -> toolchains/<latest-installed-version>
+-- local/
`-- toolchains/
    +-- 0.0.1-alpha.15/
    `-- 0.0.1/
```

Each toolchain directory should use this shape:

```text
<toolchain-root>/
+-- bin/
|   +-- ailang
|   +-- aivm
|   `-- aivectra
+-- lib/
+-- include/
+-- compiler/
+-- std/
+-- sys/
+-- runtimes/
|   +-- host/
|   |   `-- aivm
|   +-- osx-arm64/
|   |   `-- aivm
|   +-- linux-x64/
|   |   `-- aivm
|   `-- ...
+-- manifests/
|   `-- local.toml or install.toml
`-- .artifacts/
```

SDKs should bundle the supported host runtimes they can publish. `host` points
to the SDK's runtime for the current machine. Self-contained publish consumes
`<toolchain-root>/runtimes/<target>/...` and bundles only the selected target
runtime into the app output.

`~/.ailang/bin` contains stable shims. The shims dispatch to
`~/.ailang/current`, so switching released SDKs does not require changing
`PATH`. `~/.ailang/current` is for the latest installed versioned SDK; mutable
local development builds are selected per project.

## Project Toolchain Selection

Projects can select a toolchain without changing the global current toolchain by
committing an `ailang-toolchain.toml` file at the project root:

```toml
[toolchain]
version = "local"
```

The stable shims under `~/.ailang/bin` search from the current working
directory up to the filesystem root for `ailang-toolchain.toml`. If found, the
selected version resolves to:

```text
~/.ailang/toolchains/<version>
```

The special value `local` resolves to:

```text
~/.ailang/local
```

If no project selection exists, the shims fall back to `~/.ailang/current`.
`AILANG_TOOLCHAIN=<version>` can be used as a temporary command-level override.

Use the helper script to write or inspect the project selection:

```bash
cd AiLang
./scripts/select-toolchain.sh local ../AiVectra
./scripts/select-toolchain.sh --show ../AiVectra
./scripts/select-toolchain.sh --clear ../AiVectra
```

## Local Development SDK

Local development builds use the reserved toolchain slot:

```text
~/.ailang/local
```

The local slot is intentionally mutable. It is for active development only and
must not be published as a release artifact. When a bug is fixed in AiVM while
working in AiVectra, rebuild and restage the local SDK:

```bash
cd AiLang
./scripts/update-local-toolchain.sh
```

That command rebuilds the sibling AiVM checkout when present, rebuilds AiLang,
stages AiVectra source/launcher when present, and updates `~/.ailang/local`.
It does not change `~/.ailang/current`.
After that, commands run from projects selecting `local` resolve through the
refreshed local SDK:

```bash
aivm --help
ailang --version
aivectra --help
```

To use the local SDK for one project without changing every shell, write a
project-local selector:

```bash
cd AiLang
./scripts/select-toolchain.sh local ../AiLang
./scripts/select-toolchain.sh local ../AiVM
./scripts/select-toolchain.sh local ../AiVectra
```

Any `ailang`, `aivm`, or `aivectra` command run from those project directories
will resolve through `~/.ailang/local`.

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

## Current Independence Rules

- AiVM must build and test without AiLang or AiVectra.
- AiLang must use an installed AiVM executable for normal development and CI.
- AiLang source-level VM tests may use `AIVM_C_SOURCE_DIR`, but this is an
  explicit VM-development mode.
- Sibling checkout fallback is disabled by default. During migration, set
  `AILANG_ALLOW_SIBLING_AIVM_SOURCE=1` to use `../AiVM/native`.
- AiVectra is an AiLang project. It should depend on an installed `ailang`
  toolchain, not on AiVM internals.

## Immediate Implementation Tasks

1. Add `version` to AiLang and AiVectra root `project.aiproj` files.
2. Keep AiVM versioned from `native/CMakeLists.txt`.
3. Make release workflows derive versions from the component's canonical
   version source.
4. Add generated `install.toml` to release artifacts.
5. Add `--version` output that reports component version and ABI version.
6. Add installer validation before any global install command writes files.
