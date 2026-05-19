# Stdlib Production Tasks

This branch exists to turn the baseline in
`Docs/Launch-Stdlib-0.0.1.md` into an enforced repo contract.

## Phase 1: Namespace cleanup

Status: duplicate stdlib module cleanup is enforced. `src/std/platform.aos` is
forbidden by `scripts/test-stdlib-conformance.sh`; `std.system` owns platform
identity.

1. Audit every application-facing library entrypoint still living in
   `src/compiler/`.
2. Move app-facing helpers into `src/std/` or mark them toolchain-only.
3. Update docs and samples to prefer `std.*` only.
4. Do not introduce aliases or fallback library names while contract negotiation
   is still open before `1.0.0`.

## Phase 2: Baseline conformance

Status: baseline structure is now enforced by manifest, capability matrix, and
behavior checks. Deterministic helpers and the first-pass effectful wrappers are
covered by `scripts/test-stdlib-behavior.sh`.

1. Keep `Docs/Stdlib-Baseline-Manifest.tsv` as the required module/export
   contract.
2. Keep `scripts/test-stdlib-conformance.sh` failing if baseline modules
   disappear or exports change without an explicit contract update.
3. Keep `scripts/test-stdlib-capabilities.sh` failing if capability rows drift
   from the baseline.
4. Keep `scripts/test-stdlib-behavior.sh` covering deterministic baseline
   behavior and first-pass effectful wrapper behavior for:
   - `std.core`
   - `std.str`
   - `std.bytes`
   - `std.null`
   - `std.number`
   - `std.math`
   - `std.io`
   - `std.debug`
   - `std.fs`
   - `std.process`
   - `std.system`
   - `std.time`
5. Keep wasm capability notes for each baseline module:
   - native
   - wasm cli
   - wasm spa
   - wasm fullstack

## Phase 3: Library quality

1. Review `std.debug.aos` for production-readiness:
   - stable stderr logging surface
   - production assertion behavior
   - debug/profile-only replay/capture expectations
   - target capability notes
   - contract distinction between production diagnostics and debug/profile-only
     internals
1. Review the `std-http` package for production-readiness:
   - request/response helpers
   - error shape
   - timeout/cancel behavior
   - target capability notes
2. Review the `std-json` package for production-readiness:
   - parse coverage
   - stringify stability
   - result-node contract consistency
3. Review `std.fs.aos`, `std.process.aos`, and `std.time.aos` for surface
   minimality and consistency beyond first-pass behavior coverage.

## Phase 4: Sample adoption

1. Remove direct `sys.*` usage from samples where a baseline `std.*` wrapper
   should exist.
2. Ensure samples demonstrate the intended library surface instead of host
   internals.

## Exit condition

This branch is complete when:

- the production baseline is explicitly documented
- baseline modules are enforced by tests or conformance checks
- baseline deterministic helper behavior is covered by execution tests
- samples prefer `std.*`
- `std.debug` is treated as required production surface
- duplicate/alias stdlib surfaces are removed rather than preserved
- `compiler.*` is no longer acting as an app-library dumping ground
