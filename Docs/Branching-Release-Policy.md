# Branching and Release Policy

## Branch roles

1. `develop`
- Integration branch for ongoing work.

2. `main`
- Release branch only.
- Must always be in releasable state.

## Merge policy

- No direct pushes to `main`.
- `main` changes only through pull requests.
- Required checks must pass before merge:
  - `Main Release Gate / build-*`
  - `Main Release Gate / golden-gate-linux`

## Release policy

- Releases are cut from `main` only.
- Tags are created from `main` release commits.
- Release artifacts come from CI workflows.
