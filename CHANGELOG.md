# Changelog

All notable changes to this project are documented in this file.

## [0.0.1] - 2026-02-26

### Added
- Standardized CLI wrapper parsing contract for `run` and `debug`:
  - implicit `project.aiproj` resolution from current directory
  - explicit app/project target without required `--`
  - canonical `--` passthrough for app args
  - legacy `|` separator compatibility (deprecated)
- Non-invasive debug scenario flow with TOML fixture inputs and TOML artifact outputs.
- Agent-facing docs:
  - `Docs/Agent-Debug-Workflow.md`
  - `Docs/CLI-Wrapper-Contract.md`
  - `Docs/Launch-Checklist.md`

### Changed
- CLI help now documents standardized syntax patterns and legacy separator deprecation.
- Debug data surfaces now use TOML for fixture/scenario/artifact data.

### Notes
- This is a pre-1.0 compatibility-breaking baseline release.
- Runtime semantics were not changed by wrapper parser standardization.
