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
- [Project Layout](./Project-Layout.md)
- [Host Boundary](./HOST_BOUNDARY.md)
- [Agent Code Map](./Agent-CodeMap.md)
- [Conventions](./Conventions.md)
- [Agent Debug Workflow](./Agent-Debug-Workflow.md)
- [C VM Test/Profile/Benchmark Workflow](./C-VM-Performance-Workflow.md)
- [Production Memory Readiness](./Production-Memory-Readiness.md)
- [WASM Remote Transport and Fullstack Stdio Task](./Wasm-Remote-Transport-And-Stdio-Task.md)
- [WASM Limitations and Matrix](./Wasm-Limitations-And-Matrix.md)
- [CLI Wrapper Contract](./CLI-Wrapper-Contract.md)
- [AiLang vs AiVectra Boundary](./AiLang-AiVectra-Boundary.md)
- [Branching and Release Policy](./Branching-Release-Policy.md)
- [Versioning](./Versioning.md)
- [Migration 0.0.1](./Migration-0.0.1.md)
- [Launch Stdlib 0.0.1](./Launch-Stdlib-0.0.1.md)
- [Release 0.0.1](./Release-0.0.1.md)
- [Launch Checklist](./Launch-Checklist.md)
- [Draft Concurrency Contract](../SPEC/CONCURRENCY.md)

## Hard Constraints

- AOS only for AiLang language and runtime contracts. Repository tooling metadata may still use host-tool-native formats when appropriate.
- Deterministic behavior and output.
- No hidden side effects.
- No semantic drift from `SPEC/`.

## Agent Operating Rule

- Prefer improving AiLang and AiVectra built-in tooling over working around missing capability with manual steps or brittle external helpers.
- If a task cannot be completed cleanly with the current debug, automation, or diagnostic surface, extend the toolchain first.
- Treat repeated need for human verification of UI/runtime state as a tooling defect, not a normal workflow.

## Project Root Contract

Treat this repository as an AiLang project with:

- Manifest: `../project.aiproj`
- Entry compiler flow: `../src/compiler/aic.aos`
