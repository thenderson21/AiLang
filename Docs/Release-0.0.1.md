# Release 0.0.1 Readiness

## Target

- Version: `0.0.1`
- Date: 2026-02-26

## Freeze decisions

1. Spec baseline
- Launch semantic baseline is `SPEC/IL.md`, `SPEC/EVAL.md`, `SPEC/VALIDATION.md` as of this release.

2. CLI wrapper contract
- `Docs/CLI-Wrapper-Contract.md` is normative for wrapper parser behavior for this release line.

3. Launch stdlib baseline
- `Docs/Launch-Stdlib-0.0.1.md` defines supported stdlib modules for launch.

4. Branching/release flow
- `Docs/Branching-Release-Policy.md` is normative for `develop` vs `main` behavior.

## Required validation

- `dotnet build src/AiCLI/AiCLI.csproj -v minimal -m:1 /nr:false`
- `dotnet test tests/AiLang.Tests/AiLang.Tests.csproj -v minimal -m:1 /nr:false --filter "Name~CliInvocationParsing_|Name~Cli_HelpText_ContainsCommandSectionsAndExamples"`
- `./scripts/bootstrap-debug-fixtures.sh`
- `dotnet src/AiCLI/bin/Debug/net10.0/airun.dll debug scenario examples/debug/scenarios/minimal.scenario.toml --name minimal`
- `./scripts/test.sh` (full golden gate)
- GitHub Actions `Main Release Gate` workflow green on target commit

## Notes

- Any failed required validation blocks release sign-off.
