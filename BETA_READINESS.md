# AiLangCore Beta Readiness

Status: first beta release published; remaining unchecked items are hardening
and public polish for the next beta.

Beta means the project is credible for outside developers, conference demos,
and sponsor review. It does not mean API freeze. Until a major or minor release
is officially released, contracts may still change without compatibility
layers.

## Current Beta Release

- AiLang: `v0.0.1-beta.2`
- AiVM: `v0.0.1-beta.1`
- AiVectra: `v0.0.1-beta.1`

Verified on 2026-05-19:

- GitHub release metadata marks all three releases as prereleases.
- AiLang release workflow passed Linux, macOS, Windows, WASM, package, and
  release-publish jobs.
- AiVM release workflow passed Linux, macOS, Windows, package, and
  release-publish jobs.
- AiVectra release workflow passed SDK package and release-publish jobs.
- Live website install script defaults to the `beta` channel.
- Local macOS install smoke succeeded from `https://ailang.codes/install.sh`.
- Installed SDK smoke succeeded for `ailang init --agent codex`, `ailang build`,
  and `ailang run`.

## Required Gates

- [ ] Deterministic golden tests are stable across macOS, Linux, and Windows.
- [ ] Install flow works from a clean machine on macOS.
- [ ] Install flow works from a clean machine on Linux.
- [ ] Install flow works from a clean machine on Windows.
- [x] `ailang init` works with project templates and `--agent` options.
- [x] `ailang build` works from an installed SDK.
- [x] `ailang run` works from an installed SDK.
- [ ] `ailang test` exists or the beta docs explicitly define the temporary
  test command.
- [x] Package restore works from the curated package registry.
- [ ] Package restore rejects tool command conflicts deterministically.
- [ ] Package publishing flow is documented.
- [x] AiVM native runtime is the runtime used by the public SDK.
- [x] `aivm` and `aivm-debug` release artifacts exist for supported hosts.
- [x] At least one AiVectra sample app is functional and documented.
- [ ] Canonical formatting is stable enough for docs and samples.
- [ ] Resource limits are documented and visible in diagnostics.
- [ ] Error code families are documented and stable for beta.

## Public Coherence Gates

- [ ] `develop` and `main` branch story is clear for each public repository.
- [ ] Default branch or README status points at the current architecture.
- [x] Website install instructions match the latest published artifacts.
- [x] GitHub release metadata matches the website version.
- [ ] Each main repository has a short description, topics, install section,
  architecture summary, current status, and roadmap link.

## Spec Ownership Gates

- [ ] AiLang owns language semantics, IL, evaluation, validation, async/task
  semantics, and concurrency semantics.
- [ ] AiVM owns runtime implementation, memory mechanics, scheduling mechanics,
  event queue mechanics, and syscall dispatch.
- [ ] AiVectra owns UI runtime semantics, vector rendering, and app runtime
  integration.
- [ ] Duplicate specs are removed or replaced by pointers to canonical specs.

## Package Ecosystem Gates

- [x] One canonical package demo exists.
- [x] Demo uses a dependency from the curated registry.
- [x] Demo documents restore, build, and run.
- [ ] Tool packages expose subcommands without name conflicts.
- [x] Library packages are referenceable by AiLang source.
- [x] Template packages are visible through template listing.

## Sponsorship Gates

- [ ] Public roadmap explains Alpha -> Beta -> RC -> 1.0.
- [ ] Website explains funding goals.
- [ ] Funding goals name concrete work: CI, release automation,
  deterministic tests, cross-platform packaging, documentation, AiVM native
  runtime hardening, and AiVectra stabilization.
- [x] Conference/demo path is documented: install, initialize with Codex,
  build, run, and show an AiVectra sample.

## Next Beta Hardening Tasks

1. Run fresh Linux and Windows installer smoke tests against the published beta
   artifacts.
2. Define or implement the beta `ailang test` command behavior.
3. Finish public package publishing documentation.
4. Audit package restore from an installed SDK using the public curated
   registry.
5. Tighten repo metadata and README status across the public repositories.
6. Finish resource-limit and error-code documentation for beta users.
7. Decide whether failed AiLang `v0.0.1-beta.1` should remain visible as a
   historical failed tag or be removed manually.

## Beta Exit Rule

The first beta is published. Future beta releases should reduce the unchecked
hardening and public coherence items until the project is ready for release
candidate work.
