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
- Current: `Yellow`
- Notes: Specs exist and have substantial detail; final "frozen for launch" decision is not yet recorded.

2. Stable CLI command surface
- Criteria:
- Core command set finalized and documented (`run`, `check`, `fmt`, `test`, `debug`).
- Help text and docs align with behavior.
- Current: `Yellow`
- Notes: `debug` command and docs exist; final surface freeze still pending.

3. Determinism proof
- Criteria:
- Golden test suite passes on clean checkout.
- Replay/debug artifacts are byte-stable for same inputs.
- Current: `Yellow`
- Notes: replay/debug scenario path is deterministic in local targeted checks; full golden pass is not yet confirmed in this session.

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
- Current: `Yellow`
- Notes: substantial stdlib exists; launch-tier/API freeze list not yet explicitly published.

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
- Current: `Yellow`
- Notes: `scripts/test-debug-ci-parity.sh` exists; full end-to-end completion still needs confirmation in a clean environment.

9. Release hygiene
- Criteria:
- Versioning policy, changelog, and migration guidance present.
- Install/build steps verified on target platforms.
- Current: `Gray`
- Notes: not fully assessed in this pass.

## Verified In This Pass

- `dotnet build src/AiCLI/AiCLI.csproj -v minimal -m:1 /nr:false` passed.
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

1. Run and record full unbounded `scripts/test.sh` result from clean checkout.
2. Publish explicit launch freeze decision for specs + CLI surface.
3. Publish launch-tier stdlib/API list (supported vs experimental).
4. Complete release hygiene set (versioning/changelog/migration/install verification).
