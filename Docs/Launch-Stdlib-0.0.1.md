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
The baseline is enforced by `Docs/Stdlib-Baseline-Manifest.tsv` plus
`scripts/test-stdlib-conformance.sh`.

Per-target support expectations for those baseline modules are declared in
`Docs/Stdlib-Capability-Matrix.tsv` and validated by
`scripts/test-stdlib-capabilities.sh`.

### Tier 1: Language core

- `src/std/core.aos`
- `src/std/io.aos`
- `src/std/str.aos`
- `src/std/bytes.aos`
- `src/std/null.aos`
- `src/std/number.aos`
- `src/std/math.aos`

Reason:

- These provide the minimum data/result/string/printing utilities expected from
  a production language.
- `std.number` owns deterministic integer comparison, formatting, parsing, and
  basic signed arithmetic helpers.
- `std.math` is the arithmetic growth point. The current baseline includes
  deterministic integer `add`, `sub`, `mul`, and `negate`; division and modulo
  are intentionally deferred until AiLang has a settled numeric error/result
  contract.

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
  supported diagnostics surface. In the production VM this is intentionally
  limited to mode detection, stderr logging, and assertions.

## Production diagnostics baseline

Production diagnostics must remain small enough for the stripped production VM:

- `std.io.write` and `std.io.writeLine` write to stdout.
- `std.io.writeErrLine` writes to stderr.
- `std.debug.debugMode` returns the active debug/profile mode string.
- `std.debug.log(level, message)` writes a structured line to stderr.
- `std.debug.info`, `std.debug.warn`, and `std.debug.error` are level-specific
  wrappers over `std.debug.log`.
- `std.debug.debugAssert` is the standard assertion hook.

Capture, replay, artifact writing, async tracing, frame capture, and UI replay
are debug/profile-runtime features. They are still valid host/debug tooling
capabilities, but they are not production stdlib exports.

## Not part of the production baseline

These libraries are first-party packages, not minimum stdlib modules.

- `std-json`
- `std-net`
- `std-http`
- `std-image`
- `std-ui-input`
  - Useful, but profile-specific and expected to live in an optional package or
    AiVectra.
- Debug/profile-only capture APIs:
  - `captureFrameBegin`
  - `captureFrameEnd`
  - `captureDraw`
  - `captureInput`
  - `captureState`
  - `replayLoad`
  - `replayNext`
  - `artifactWrite`
  - `traceAsync`
  - These belong to debug/profile tooling or AiVectra-specific packages, not
    the production stdlib.
- `src/std/platform.aos`
  - Redundant with `std.system.platform` and must not remain in the shipped
    stdlib surface as an alias.

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

## Numeric baseline

The `0.0.1` numeric baseline is intentionally integer-first:

- `std.number` owns general number helpers:
  - constants: `zero`, `one`
  - arithmetic: `add`, `sub`, `mul`, `inc`, `dec`, `negate`, `abs`
  - comparison: `equals`, `lt`, `lte`, `gt`, `gte`
  - state/range helpers: `isZero`, `isNegative`, `isPositive`,
    `isNonNegative`, `sign`, `min`, `max`, `clamp`, `betweenInclusive`
  - parsing/formatting: `toString`, `digitValue`, `isWholeString`,
    `parseWholeOr`, `isSignedWholeString`, `isNumberString`, `parseNumberOr`
- `std.math` owns arithmetic helpers intended to grow into the broader math
  namespace.

Division, modulo, floating point, overflow policy, and decimal behavior are not
part of this baseline. Add them only after the numeric failure/result behavior
is specified.

## Contract policy

- Contract negotiation remains open until `1.0.0`.
- Pre-`1.0.0`, do not preserve aliases or fallback surfaces just for backward
  compatibility.
- If a name or shape is wrong, replace it cleanly and update repo call sites in
  the same change.

## Baseline quality bar

Each production-baseline module must meet all of these:

- Lives under `src/std/`.
- Has a clear purpose and naming boundary.
- Avoids exposing raw `sys.*` details when a library-level abstraction is
  warranted.
- Behaves deterministically.
- Has target-specific unsupported behavior documented when capability-limited.
- Declares target capability status in `Docs/Stdlib-Capability-Matrix.tsv`.
- Has coverage through library tests, golden coverage, or sample usage.
- Has behavior coverage for required deterministic helpers through
  `scripts/test-stdlib-behavior.sh` where practical.
- For debugging libraries, has clear production behavior for capture, replay,
  and diagnostic emission. Production `std.debug` is limited to stderr logging,
  debug mode, and assertions.

## Immediate cleanup targets

1. Make `std.*` the only intended application library namespace.
2. Remove duplicate wrappers such as `src/std/platform.aos` where a broader
   module already owns the contract.
3. Audit first-party packages for any lingering dependency on
   `compiler.*`-shaped app behavior.
4. Define per-target support expectations for baseline modules, especially wasm
   profiles.
5. Add conformance tests so the baseline is enforced by the repo, not just by
   documentation.
6. Treat the limited production `std.debug` surface as required. Keep capture,
   replay, trace, and artifact helpers out of the production stdlib contract.

## JSON contract

`std-json` remains first-party with intentionally constrained behavior:

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
