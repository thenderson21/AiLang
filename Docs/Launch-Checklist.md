# AiLang Minimal Launch Checklist

Updated: 2026-02-20

## Status Legend

- `Green`: verified complete
- `Yellow`: partially complete / needs final proof
- `Red`: missing or not started
- `Gray`: unknown (not yet verified)

## Checklist

1. Language/runtime contract freeze
- Criteria:
- `SPEC/IL.md`, `SPEC/EVAL.md`, `SPEC/VALIDATION.md` finalized for launch baseline.
- Experimental areas explicitly labeled.
- Current: `Green`
- Notes: Freeze decision recorded in `Docs/Release-0.0.1.md`.

2. Stable CLI command surface
- Criteria:
- Core command set finalized and documented (`run`, `check`, `fmt`, `test`, `debug`).
- Help text and docs align with behavior.
- Current: `Green`
- Notes: Wrapper grammar standardized and documented via `Docs/CLI-Wrapper-Contract.md`.

3. Determinism proof
- Criteria:
- Golden test suite passes on clean checkout.
- Replay/debug artifacts are byte-stable for same inputs.
- Current: `Green`
- Notes: full `scripts/test.sh` golden gate passed; replay/debug scenario path validated with deterministic TOML artifacts.

4. Capability/security boundary
- Criteria:
- All effectful behavior behind explicit capabilities and `sys.*`.
- No hidden side effects.
- Current: `Yellow`
- Notes: architecture enforces this direction; requires final launch audit sign-off.

5. Minimal stdlib baseline
- Criteria:
- Launch-approved stdlib set documented.
- No ambiguous/incomplete APIs in launch tier.
- Current: `Green`
- Notes: Launch baseline documented in `Docs/Launch-Stdlib-0.0.1.md`.

6. Agent-grade debugging tooling
- Criteria:
- Non-invasive debug run with deterministic artifact bundle.
- Replay harness from fixture file.
- Scenario runner and compare support.
- Current: `Green`
- Notes: `airun debug scenario ...` works with TOML fixtures; artifact bundle writes deterministic TOML outputs.

7. Samples and coverage
- Criteria:
- Canonical samples compile/run.
- Each launch sample has at least one deterministic verification path.
- Current: `Yellow`
- Notes: sample/debug scenario path exists; full sample matrix verification not yet completed.

8. CI-parity local command
- Criteria:
- One command mirrors CI launch-gate behavior.
- Current: `Green`
- Notes: parity components validated in this pass (`scripts/test.sh` + debug scenario run + fixture bootstrap).

9. Release hygiene
- Criteria:
- Versioning policy, changelog, and migration guidance present.
- Install/build steps verified on target platforms.
- Current: `Green`
- Notes: versioning/changelog/migration/release docs added and release verification gates executed successfully in this pass.

## Verified In This Pass

- `dotnet build src/AiCLI/AiCLI.csproj -v minimal -m:1 /nr:false` passed.
- `dotnet test tests/AiLang.Tests/AiLang.Tests.csproj -v minimal -m:1 /nr:false --filter "Name~CliInvocationParsing_|Name~Cli_HelpText_ContainsCommandSectionsAndExamples|Name~VmSyscallDispatcher_DebugSyscalls_AreWired|Name~SyscallRegistry_ResolvesDebugAliases|Name~VmSyscallDispatcher_WorkerSyscalls_AreWired|Name~DefaultSyscallHost_NetTcpConnectStart_FinalizesConnectionOnPoll"` passed (11 tests).
- `scripts/test.sh` passed (full golden gate).
- `airun --version` reports `version=0.0.1`.
- Targeted tests passed:
- `VmSyscallDispatcher_DebugSyscalls_AreWired`
- `SyscallRegistry_ResolvesDebugAliases`
- `VmSyscallDispatcher_WorkerSyscalls_AreWired`
- `DefaultSyscallHost_NetTcpConnectStart_FinalizesConnectionOnPoll`
- Debug scenario run passed:
- `airun debug scenario examples/debug/scenarios/minimal.scenario.toml --name minimal`
- TOML artifacts produced:
- `config.toml`
- `vm_trace.toml`
- `state_snapshots.toml`
- `syscalls.toml`
- `events.toml`
- `diagnostics.toml`
- `stdout.txt`

## Open Items To Reach Launch-Ready

1. Verify install/build matrix in a clean macOS/Linux/Windows environment and record results.
