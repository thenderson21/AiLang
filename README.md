# AiLang

AiLang is an AI-first language/runtime project using AI-Optimized Syntax (AOS) as its canonical representation.

![AiLang](assets/AiLang.png)

This root README is human-oriented.  
For agent-focused operating docs, use `Docs/README.md`.

## Project Identity

This repository is itself an AiLang project.

- Project manifest: `project.aiproj`
- Compiler driver: `src/compiler/aic.aos`
- Standard library: `src/std/*.aos`

## Quick Start

Use the repo-local launcher:

```bash
./tools/airun repl
```

Run program execution uses the AiBC1 VM by default:

```bash
./tools/airun run examples/hello.aos
```

Force AST interpreter mode for debugging only:

```bash
./tools/airun run --vm=ast examples/hello.aos
```

Load a program and evaluate expressions:

```text
Cmd#c1(name=load) { Program#p1 { Let#l1(name=x) { Lit#v1(value=1) } } }
Cmd#c2(name=eval) { Call#c3(target=math.add) { Var#v2(name=x) Lit#v3(value=2) } }
```

## Permissions

Capabilities are gated by permissions:

- `math.add` is pure and allowed by default.
- `console.print` is effectful and denied by default.

Enable console output in the REPL:

```text
Cmd#c9(name=setPerms allow=console,math)
```

## REPL Transcript (Example)

```text
Cmd#c1(name=help)
Ok#ok6(type=void) { Cmd#cmd1(name=help) Cmd#cmd2(name=setPerms) Cmd#cmd3(name=load) Cmd#cmd4(name=eval) Cmd#cmd5(name=applyPatch) }
Cmd#c2(name=setPerms allow=console,math)
Ok#ok2(type=void)
Cmd#c3(name=load) { Program#p1 { Let#l1(name=message) { Lit#s1(value="hi") } Call#c1(target=console.print) { Var#v1(name=message) } } }
Ok#ok3(type=void)
Cmd#c4(name=eval) { Call#c2(target=math.add) { Lit#a1(value=2) Lit#a2(value=3) } }
Ok#ok4(type=int value=5)
```

## Testing

Run golden tests without dotnet:

```bash
./scripts/test.sh
```

`scripts/test.sh` uses only `./tools/airun`; it does not invoke dotnet.

## Build Launcher

Rebuild `tools/airun` (NativeAOT, osx-arm64):

```bash
./scripts/build-airun.sh
```

Dotnet is only required for `scripts/build-airun.sh`.
`scripts/build-airun.sh` also rebuilds the standalone frontend parser `tools/aos_frontend`.

## Runtime Engine

- Canonical runtime: AiBC1 bytecode VM (default).
- AST interpreter: debug-only fallback via `--vm=ast`.
- New publish artifacts embed bytecode payloads by default.

## Examples

See `examples/hello.aos` for a full program using `console.print`, and `examples/golden` for evaluator/fmt/check goldens.

## Language Contracts

Normative semantic contracts live in:

- `SPEC/IL.md`
- `SPEC/EVAL.md`
- `SPEC/VALIDATION.md`

When semantics change, update these specs and matching goldens together.
