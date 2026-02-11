# Overview

## Purpose

Define repository components and stable operating assumptions for agents.

## Preconditions

- Working directory is repo root.
- `tools/airun` exists (or run `./scripts/build-airun.sh`).

## Components

- `tools/airun`: native runtime entrypoint.
- `src/compiler/aic.aos`: compiler driver (`fmt`, `check`, `run`, `test`).
- `src/compiler/format.aos`: canonical formatter.
- `src/compiler/validate.aos`: structural validator.
- `src/std/*.aos`: minimal standard library modules.
- `examples/golden/*`: deterministic golden corpus.

## Invariants

- Parsing, validation, formatting, evaluation are deterministic.
- Module imports are relative-path only.
- Semantic contracts are frozen in `SPEC/`.

## Failure Surface

- Parse/validation/runtime failures are emitted as canonical `Err#...` AOS nodes.
