# AiCLI Native

Native C CLI entrypoint for zero-C# migration.

Current:
- `airun.c` provides a deterministic native wrapper executable for `tools/airun`.
- `scripts/build-airun.sh` compiles this wrapper and preserves the existing backend host at `tools/airun-host`.
- `run --vm=c` bridge loading is handled directly by `airun.c` (no helper script dependency).
- Wrapper source is now portable (POSIX + Windows compile path), but Windows runtime packaging remains blocked until a tracked backend host replacement exists.

Target end-state:
- CLI arg parsing and mode selection
- syscall host binding
- direct delegation to native core/vm layers (no backend-host dependency)
