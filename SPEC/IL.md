# IL Contract

This file is normative for the executable AiLang IL subset used by `aic run`.

## Core Node Kinds

| Kind | Required attrs | Child arity | Notes |
|---|---|---:|---|
| `Program` | none | `0..N` | Evaluate children in order. |
| `Block` | none | `0..N` | Evaluate children in order. |
| `Let` | `name` (identifier) | `1` | Binds evaluated child result to `name`. |
| `Var` | `name` (identifier) | `0` | Reads from environment. |
| `Lit` | `value` (`string\|int\|bool`) | `0` | Literal value node. |
| `Call` | `target` (identifier/dotted identifier) | `0..N` | Native or user-defined call. |
| `Import` | `path` (string, relative) | `0` | Loads another module and merges explicit exports. |
| `Export` | `name` (identifier) | `0` | Exposes one binding from current module. |
| `Project` | `name` (string), `entryFile` (string), `entryExport` (string) | `0..N` | Project manifest node for `project.aiproj`; children must be `Include`. |
| `Include` | `name` (string), `path` (string, relative), `version` (string) | `0` | Declares library include metadata for project-level dependency resolution. |
| `If` | none | `2..3` | Condition, then-branch, optional else-branch. |
| `Eq` | none | `2` | Value equality. |
| `StrConcat` | none | `2` | String concatenation. |
| `Add` | none | `2` | Integer addition. |
| `Fn` | `params` (identifier) | `1` | Function literal with captured env. |

## Value Shapes

- Runtime values are represented as nodes:
- `Lit(value=...)` for primitive values.
- `Block#void` as the canonical void value.
- `Err(code=... message="..." nodeId=...)` for runtime errors.
- Function values are closures represented as block nodes containing:
- function node + captured environment.

## Stability Rule

- Changes to kind set, attrs, arity, or value shape require updates to:
- `SPEC/IL.md`
- related golden tests under `examples/golden`
