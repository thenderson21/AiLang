# AiLang.Core

Language-layer implementation for AiLang.

## Responsibilities

- AOS parsing and tokenization.
- AST/IL model and validation.
- Canonical formatter host wiring.
- Interpreter semantics (split across `AosInterpreter.*.cs` partials).
- Compiler asset loading and golden harness support.

## Non-responsibilities

- Direct syscall implementation details (kept in `AiVM.Core`).
- CLI argument parsing and process boot (kept in `AiCLI`).

## Interpreter Partial Map

- `AosInterpreter.cs`: public entrypoints and eval loop.
- `AosInterpreter.CoreEval.cs`: node-kind dispatch.
- `AosInterpreter.Calls.cs`: top-level call routing.
- `AosInterpreter.CompilerCalls.cs`: `compiler.*` call handling.
- `AosInterpreter.SysBridge.cs`: `sys.*` and capability bridge.
- `AosInterpreter.Imports.cs`: module import and bytecode import flattening.
- `AosInterpreter.Publish.cs` + `AosInterpreter.ProjectPublish.cs`: publish flow helpers.
- `AosInterpreter.NodeOps.cs`: node/attr helpers and primitive node ops.

Keep semantic behavior deterministic and aligned with `SPEC/`.
