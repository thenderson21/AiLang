# AiCLI Native

Native C CLI entrypoint for zero-C# migration.

Current:
- `airun.c` provides the deterministic native C runtime executable for `tools/airun`.
- C VM is the default runtime (no flag required).
- `--vm=c` is an explicit alias of the default runtime.
- `--vm=cvN` is reserved for future C VM profile/version selection; currently it maps to `c`.
- `--vm=ast` remains debug-only and is not supported by native runtime execution.
- `scripts/build-airun.sh` compiles Unix targets (`osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64`).
- `scripts/build-airun.ps1` compiles Windows targets (`windows-x64`, `windows-arm64`).
- `.aibc1` runtime execution is C-only.
- Source/project `run` flows are native-only. Unsupported source shapes return deterministic `DEV008` guidance (no backend delegation).
- `.aibundle` runtime execution is native-only (Bytecode# bundle shape).
- Native `Bytecode#...` `.aos` inputs run directly in C VM without backend fallback.
- Native `publish` can emit `app.aibc1` from supported `Program#...`/`Bytecode#...` `.aos`; unsupported source/project compile shapes return deterministic `DEV008`.
- Native `publish --target wasm32` emits wasm runtime package outputs:
  - profile `spa`/`web` (default): `<app>.wasm`, `app.aibc1`, `aivm-runtime-wasm32-web.mjs`, `index.html`, `main.js`
  - profile `cli`: `<app>.wasm`, `app.aibc1`, `run.sh`, `run.ps1`
  - profile `fullstack`: root wasm payload plus `client/` web package and `server/README.md`
- Native `Program#...`/`Bytecode#...` supported subsets run/publish without backend fallback.
- `serve` is intentionally not part of native runtime surface; native runtime returns deterministic `DEV008` for `serve`.
- `publish` writes a ready-to-run app executable named from project/app input (run as `./<appname>`), plus `app.aibc1`.
- `project.aiproj` can set publish default target via `publishTarget="<rid>"` (or single-entry `publishTargets="..."`).

Target end-state:
- CLI arg parsing and mode selection
- syscall host binding
- direct native core/vm execution only (no backend-host dependency)
