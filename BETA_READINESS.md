# AiLangCore Beta Readiness

Status: project-wide checklist for AiLang, AiVM, and AiVectra.

Beta means the project is credible for outside developers, conference demos,
and sponsor review. It does not mean API freeze. Until a major or minor release
is officially released, contracts may still change without compatibility
layers.

## Required Gates

- [ ] Deterministic golden tests are stable across macOS, Linux, and Windows.
- [ ] Install flow works from a clean machine on macOS.
- [ ] Install flow works from a clean machine on Linux.
- [ ] Install flow works from a clean machine on Windows.
- [ ] `ailang init` works with project templates and `--agent` options.
- [ ] `ailang build` works from an installed SDK.
- [ ] `ailang run` works from an installed SDK.
- [ ] `ailang test` exists or the beta docs explicitly define the temporary
  test command.
- [ ] Package restore works from the curated package registry.
- [ ] Package restore rejects tool command conflicts deterministically.
- [ ] Package publishing flow is documented.
- [ ] AiVM native runtime is the runtime used by the public SDK.
- [ ] `aivm` and `aivm-debug` release artifacts exist for supported hosts.
- [ ] At least one AiVectra sample app is functional and documented.
- [ ] Canonical formatting is stable enough for docs and samples.
- [ ] Resource limits are documented and visible in diagnostics.
- [ ] Error code families are documented and stable for beta.

## Public Coherence Gates

- [ ] `develop` and `main` branch story is clear for each public repository.
- [ ] Default branch or README status points at the current architecture.
- [ ] Website install instructions match the latest published artifacts.
- [ ] GitHub release metadata matches the website version.
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

- [ ] One canonical package demo exists.
- [ ] Demo uses a dependency from the curated registry.
- [ ] Demo documents restore, build, and run.
- [ ] Tool packages expose subcommands without name conflicts.
- [ ] Library packages are referenceable by AiLang source.
- [ ] Template packages are visible through template listing.

## Sponsorship Gates

- [ ] Public roadmap explains Alpha -> Beta -> RC -> 1.0.
- [ ] Website explains funding goals.
- [ ] Funding goals name concrete work: CI, release automation,
  deterministic tests, cross-platform packaging, documentation, AiVM native
  runtime hardening, and AiVectra stabilization.
- [ ] Conference/demo path is documented: install, initialize with Codex,
  build, run, and show an AiVectra sample.

## Beta Exit Rule

Beta is ready only when the required gates are checked and the public coherence
gates have no contradictions. Sponsor-facing docs may be published before beta,
but they must clearly say alpha until these gates are complete.
