# AiLang Docs (Agent-Oriented)

## Objective

Provide deterministic, execution-focused documentation for AI agents operating this repository.

This README is agent-oriented by design.
The root `README.md` is human-oriented.

## Normative Source

- `../SPEC/IL.md`
- `../SPEC/EVAL.md`
- `../SPEC/VALIDATION.md`
- `../SPEC/BYTECODE.md`

If a doc in `Docs/` conflicts with `SPEC/`, follow `SPEC/`.

## Index

- [Overview](./Overview.md)
- [Getting Started](./Getting-Started.md)
- [Host Boundary](./HOST_BOUNDARY.md)
- [Agent Code Map](./Agent-CodeMap.md)
- [Conventions](./Conventions.md)
- [Draft Concurrency Contract](../SPEC/CONCURRENCY.md)

## Hard Constraints

- AOS only (no JSON anywhere).
- Deterministic behavior and output.
- No hidden side effects.
- No semantic drift from `SPEC/`.

## Project Root Contract

Treat this repository as an AiLang project with:

- Manifest: `../project.aiproj`
- Entry compiler flow: `../src/compiler/aic.aos`
