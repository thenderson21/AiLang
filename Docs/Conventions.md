# Conventions

## File Organization

- Keep public entry types near file top.
- Prefer splitting large implementation surfaces by responsibility:
  - example: native host adapters in dedicated `airun_*_host.inc` files and VM subsystems in `aivm_*` source files.

## Naming

- `Aos*` for language/runtime structures in `AiLang.Core`.
- `Vm*` or `Host*` for VM and host boundary logic in `AiVM.Core`.
- Use explicit suffixes for intent: `*Loader`, `*Runner`, `*Adapter`, `*Publisher`.

## Determinism Rules

- Use `StringComparer.Ordinal` for all key/order-sensitive dictionaries/sets.
- Sort keys explicitly when serialization/formatting order matters.
- Never depend on process-local insertion order for externally observed output.

## Refactor Policy

- Extract duplicate logic into one shared helper before adding new call sites.
- Prefer behavior-preserving refactors with test confirmation in same change.
- Keep host boundary calls in one place (via `Host*` wrappers), not scattered.

## Validation Checklist

- Run `./test.sh`.
- If runtime host changes are involved, also rebuild and re-run:
  - `./build.sh`
  - `./test.sh`
