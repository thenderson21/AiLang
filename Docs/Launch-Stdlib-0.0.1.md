# Launch Stdlib Baseline (0.0.1)

This defines the minimum supported stdlib surface for the `0.0.1` baseline.

## Included modules

- `src/std/io.aos`
- `src/std/str.aos`
- `src/std/math.aos`
- `src/std/system.aos`
- `src/std/net.aos`
- `src/std/http.aos`
- `src/std/ui_input.aos`
- `src/std/debug.aos`
- `src/std/fs.aos`
- `src/std/process.aos`
- `src/std/time.aos`
- `src/std/bytes.aos`
- `src/std/core.aos`
- `src/std/json.aos`

## Contract level

- Included modules are supported as launch baseline.
- APIs may still evolve in `0.x`, but changes must be documented in migration notes.

## Exclusions

- No additional higher-level framework guarantees beyond these modules.
- `src/std/json.aos` parser support is intentionally minimal in `0.0.1`:
  - supported parse inputs: `null`, booleans, numbers, quoted strings, arrays, and objects (leading/trailing whitespace allowed)
  - `parse` returns the canonical parsed root token as string (`resultOkString`)
  - `parseNode` returns a typed node tree:
    - primitive kinds: `JsonNull`, `JsonBool`, `JsonNumber`, `JsonString`
    - composite kinds: `JsonArray`, `JsonObject`
    - object fields are `JsonField` nodes (`key` attr + single value child)
  - string decode in `parseNode` supports `\\`, `\"`, `\/`, `\n`, `\r`, `\t`
  - unknown escapes are preserved losslessly (for example `\q` -> `\q`), and `\b`/`\f` are preserved as `\b`/`\f`
  - unicode escape handling in `parseNode`:
    - decodes valid `\uXXXX` BMP codepoints to UTF-8 (hex case-insensitive)
    - decodes valid surrogate pairs (`\uD83D\uDE42`) to UTF-8
    - invalid/unsupported code units (for example unmatched surrogate halves) are preserved as `\uXXXX`
  - unsupported forms return deterministic `resultErr("JSON_UNSUPPORTED", ...)`
