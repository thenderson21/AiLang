# Migration Notes: 0.0.1

## CLI wrapper parsing

This release standardizes wrapper parsing for `run` and `debug`.

### New canonical forms

- `./scripts/<tool> run [app|project-dir] [-- app-args...]`
- `./scripts/<tool> debug [wrapper-flags] [app|project-dir] [-- app-args...]`

### Behavior updates

1. Implicit cwd project
- If no explicit app path is provided, wrappers use `./project.aiproj` when present.

2. Explicit path without separator
- If explicit app/project path is present, trailing tokens are treated as app args.
- `--` is not required in explicit-path mode.

3. Canonical separator
- `--` is the official wrapper/app argument separator.

4. Legacy separator
- `|` remains accepted for compatibility only.
- Marked deprecated in help text.

### Error messages

- Missing target/cwd project:
  - `missing app path (or run from a folder containing project.aiproj)`
- Unknown wrapper flag:
  - `unknown option: <flag>`

## Debug data format

Debug fixture/scenario/artifact data is TOML:

- fixtures:
  - `examples/debug/events/*.toml`
  - `examples/debug/scenarios/*.toml`
- artifacts:
  - `config.toml`
  - `vm_trace.toml`
  - `state_snapshots.toml`
  - `syscalls.toml`
  - `events.toml`
  - `diagnostics.toml`
