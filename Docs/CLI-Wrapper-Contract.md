# CLI Wrapper Contract (Ai Projects)

## Scope
Applies to project wrappers (`scripts/ailang`, `scripts/aivm`, `scripts/aivectra`, etc.) for `run` and `debug`.

## Canonical Syntax
- `./scripts/<tool> run [app|project-dir] [-- app-args...]`
- `./scripts/<tool> debug [wrapper-flags] [app|project-dir] [-- app-args...]`

## Parsing Rules

1. App target is optional.
- If omitted, wrapper MUST use current working directory when `project.aiproj` exists.
- If omitted and no `project.aiproj` in cwd, wrapper MUST fail with a clear error.

2. Explicit app/project path does not require `--`.
- Once explicit target is found, remaining non-wrapper tokens are app args.

3. `--` is the canonical separator.
- Everything after `--` is forwarded unchanged as app args.

4. Legacy separators (if any, e.g. `|`) are compatibility-only.
- Must still work if currently supported.
- Must be marked deprecated in help output.

5. Wrapper flag handling.
- Wrapper flags are only parsed before target detection or before `--`.
- Unknown wrapper flags MUST fail fast with a clear message.

## Required Error Messages
- Missing target/cwd project:
  - `missing app path (or run from a folder containing project.aiproj)`
- Unknown wrapper flag:
  - `unknown option: <flag>`

## Help Requirements
Each wrapper help text MUST include:
- Canonical syntax above
- Example: explicit path without `--`
- Example: implicit cwd project with `--`
- Note that legacy separator is deprecated (if present)

## Compatibility
- Do not break existing explicit invocations.
- Keep runtime semantics unchanged; this contract is parser behavior only.

## Required Tests (minimum)
1. Explicit path + trailing args (no `--`)
2. Explicit path + `--` args
3. Implicit cwd project + args
4. Missing path + missing cwd project error
5. Legacy separator compatibility (if implemented)

## Acceptance
Behavior and help text are consistent across all Ai project wrappers.
