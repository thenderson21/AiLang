# Overview

## Purpose

Define repository components and stable operating assumptions for agents.

## Preconditions

- Working directory is repo root.
- `tools/airun` exists (or run `./scripts/build-airun.sh`).

## Components

- `tools/airun`: native runtime entrypoint.
  - Command surface: `build`, `run`, `publish`, `repl`, `bench`, `debug run`.
- `src/compiler/aic.aos`: compiler driver (`fmt`, `fmt --ids`, `check`, `run`, `test`).
- `src/compiler/format.aos`: canonical formatter.
- `src/compiler/validate.aos`: structural validator.
- `src/std/*.aos`: minimal standard library modules.
- `examples/golden/*`: deterministic golden corpus.

## Invariants

- Parsing, validation, formatting, evaluation are deterministic.
- Module imports are relative-path only.
- Native runtime/build paths are C-only in active workflows; no C#/DLL fallback path is required.
- Semantic contracts are frozen in `SPEC/`.

## Failure Surface

- Parse/validation/runtime failures are emitted as canonical `Err#...` AOS nodes.
- Unsupported native compile/project shapes fail deterministically with `DEV008`.
