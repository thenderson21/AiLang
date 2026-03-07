# Stdlib Production Tasks

This branch exists to turn the baseline in
`Docs/Launch-Stdlib-0.0.1.md` into an enforced repo contract.

## Phase 1: Namespace cleanup

1. Audit every application-facing library entrypoint still living in
   `src/compiler/`.
2. Move app-facing helpers into `src/std/` or mark them toolchain-only.
3. Remove or deprecate duplicate modules:
   - `src/std/platform.aos` vs `src/std/system.aos`
4. Update docs and samples to prefer `std.*` only.

## Phase 2: Baseline conformance

1. Add a stdlib conformance document or manifest that lists required modules.
2. Add tests that fail if baseline modules disappear or exports change without
   an explicit contract update.
3. Add wasm capability notes for each baseline module:
   - native
   - wasm cli
   - wasm spa
   - wasm fullstack

## Phase 3: Library quality

1. Review `std.http.aos` for production-readiness:
   - request/response helpers
   - error shape
   - timeout/cancel behavior
   - target capability notes
2. Review `std.json.aos` for production-readiness:
   - parse coverage
   - stringify stability
   - result-node contract consistency
3. Review `std.fs.aos`, `std.process.aos`, and `std.time.aos` for surface
   minimality and consistency.

## Phase 4: Sample adoption

1. Remove direct `sys.*` usage from samples where a baseline `std.*` wrapper
   should exist.
2. Ensure samples demonstrate the intended library surface instead of host
   internals.

## Exit condition

This branch is complete when:

- the production baseline is explicitly documented
- baseline modules are enforced by tests or conformance checks
- samples prefer `std.*`
- `compiler.*` is no longer acting as an app-library dumping ground
