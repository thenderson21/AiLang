# Launch Stdlib Baseline (0.0.1)

This document defines the minimum AiLang library surface that should be treated
as the production baseline for the `0.0.1` line.

The goal is not "everything currently shipped." The goal is the smallest set of
libraries that makes AiLang viable as a real programming language without
forcing app authors into `sys.*` or `compiler.*` for normal work.

## Namespace rules

- `std.*` is the application-facing library surface.
- `sys.*` is the host capability boundary and must stay low-level.
- `compiler.*` is toolchain/internal surface and is not part of the application
  stdlib baseline.

Application code should prefer `std.*`. If an app needs `compiler.*` for normal
runtime work, the library split is still wrong.

## Production baseline

These modules are the required minimum baseline for production AiLang programs.

### Tier 1: Language core

- `src/std/core.aos`
- `src/std/io.aos`
- `src/std/str.aos`
- `src/std/bytes.aos`
- `src/std/math.aos`
- `src/std/json.aos`

Reason:

- These provide the minimum data/result/string/printing utilities expected from
  a production language.
- `std.json` is included because API work requires deterministic parse and
  serialize support without pushing that burden into app code.

### Tier 2: Runtime environment

- `src/std/system.aos`
- `src/std/process.aos`
- `src/std/fs.aos`
- `src/std/time.aos`
- `src/std/debug.aos`

Reason:

- These cover platform identity, process args/env, filesystem, and time.
- They are effectful wrappers around `sys.*`, but they are still baseline
  because real programs need a stable standard entrypoint for these concerns.
- `std.debug` is baseline because a production language must provide a standard,
  supported debugging surface for diagnostics, tracing, replay, and validation.

### Tier 3: Network and service baseline

- `src/std/net.aos`
- `src/std/http.aos`

Reason:

- Production apps need a canonical networking surface.
- `std.http` should be the normal application entrypoint for HTTP work instead
  of direct `sys.net.*` usage.

## Not part of the production baseline

These libraries may remain in the repo, but they are not part of the minimum
production stdlib contract.

- `src/std/ui_input.aos`
  - Useful, but profile-specific and not required for non-UI programs.
- `src/std/platform.aos`
  - Redundant with `std.system.platform`; keep only as a temporary compatibility
    wrapper or remove once call sites are migrated.

## Compiler namespace status

The following are not baseline stdlib APIs and should not be treated as app
libraries:

- `src/compiler/aic.aos`
- `src/compiler/format.aos`
- `src/compiler/http.aos`
- `src/compiler/json.aos`
- `src/compiler/route.aos`
- `src/compiler/runtime.aos`
- `src/compiler/validate.aos`

Rules:

- `compiler.*` may support the toolchain, self-hosted compiler, formatter,
  validator, or runtime bootstrap.
- `compiler.*` must not become the place where normal application libraries
  live.
- If `std.*` requires functionality from `compiler.*`, that dependency should
  be considered technical debt unless it is purely toolchain-facing.

## Baseline quality bar

Each production-baseline module must meet all of these:

- Lives under `src/std/`.
- Has a clear purpose and naming boundary.
- Avoids exposing raw `sys.*` details when a library-level abstraction is
  warranted.
- Behaves deterministically.
- Has target-specific unsupported behavior documented when capability-limited.
- Has coverage through library tests, golden coverage, or sample usage.
- For debugging libraries, has clear production behavior for capture, replay,
  and diagnostic emission.

## Immediate cleanup targets

1. Make `std.*` the only intended application library namespace.
2. Remove or deprecate duplicate wrappers such as `src/std/platform.aos` where a
   broader module already owns the contract.
3. Audit `std.http` and `std.json` for any lingering dependency on
   `compiler.*`-shaped app behavior.
4. Define per-target support expectations for baseline modules, especially wasm
   profiles.
5. Add conformance tests so the baseline is enforced by the repo, not just by
   documentation.
6. Treat `std.debug` as a required production surface, not a best-effort dev
   helper.

## JSON contract

`src/std/json.aos` remains in baseline with intentionally constrained behavior:

- supported parse inputs: `null`, booleans, numbers, quoted strings, arrays,
  and objects (leading/trailing whitespace allowed)
- `parse` returns the canonical parsed root token as string (`resultOkString`)
- `parseNode` returns a typed node tree:
  - primitive kinds: `JsonNull`, `JsonBool`, `JsonNumber`, `JsonString`
  - composite kinds: `JsonArray`, `JsonObject`
  - object fields are `JsonField` nodes (`key` attr + single value child)
- string decode in `parseNode` supports `\\`, `\"`, `\/`, `\n`, `\r`, `\t`
- unknown escapes are preserved losslessly (for example `\q` -> `\q`), and
  `\b`/`\f` are preserved as `\b`/`\f`
- unicode escape handling in `parseNode`:
  - decodes valid `\uXXXX` BMP codepoints to UTF-8 (hex case-insensitive)
  - decodes valid surrogate pairs (`\uD83D\uDE42`) to UTF-8
  - invalid/unsupported code units (for example unmatched surrogate halves) are
    preserved as `\uXXXX`
- unsupported forms return deterministic `resultErr("JSON_UNSUPPORTED", ...)`
