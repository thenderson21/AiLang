# Versioning Policy (Pre-1.0)

## Current release line

- Baseline target: `0.0.1`
- Stability model: pre-1.0 (`0.x`) with controlled breaking changes.

## Rules

1. `0.0.x` patch
- Bug fixes, docs updates, test updates, tooling polish.
- No intentional language/runtime contract breaks.

2. `0.x.0` minor
- May include breaking changes to parser/tooling/contracts.
- Must include migration notes and changelog entries.

3. Contract-sensitive changes
- Any semantic/runtime change must update:
  - `SPEC/IL.md`
  - `SPEC/EVAL.md`
  - `SPEC/VALIDATION.md`
  - corresponding tests/goldens

## Required release artifacts

- `CHANGELOG.md` entry
- migration notes for breaking changes
- updated launch checklist status
