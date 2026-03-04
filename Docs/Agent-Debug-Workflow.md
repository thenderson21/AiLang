# Agent Debug Workflow

This workflow is tooling-only. Do not modify app source to debug runtime behavior.

## Commands

1. Bootstrap fixtures from a clean checkout:

```bash
./scripts/bootstrap-golden-publish-fixtures.sh
```

2. Run app with artifact capture:

```bash
./tools/airun debug /absolute/or/relative/path/to/app.aos --out .artifacts/debug/my-run
```

3. Run deterministic replay from event fixture:

```bash
./tools/airun debug /absolute/or/relative/path/to/app.aos --events examples/debug/events/minimal.events.toml --out .artifacts/debug/replay-run
```

4. Run one named scenario fixture:

```bash
./tools/airun debug scenario examples/debug/scenarios/minimal.scenario.toml --name minimal
```

5. CI-parity local path:

```bash
./scripts/test-debug-ci-parity.sh
```

## Artifact Bundle

Each debug run writes one directory with deterministic files:

- `config.toml`: run configuration + exit status
- `stdout.txt`: canonical CLI output
- `vm_trace.toml`: step trace (`nodeId`, `op`, `function`, `pc`)
- `state_snapshots.toml`: stack/locals/env snapshots
- `syscalls.toml`: syscall args/results
- `events.toml`: lifecycle/input events (includes `sys.ui.pollEvent`)
- `diagnostics.toml`: deterministic diagnostics captured during run

## Scenario Fixture Format

Scenario fixtures are TOML files with `[[scenario]]` rows:

```toml
[[scenario]]
name = "my-case"
app_path = "../apps/debug_minimal.aos"
vm = "bytecode"
debug_mode = "replay"
events_path = "../events/minimal.events.toml"
compare_path = "../golden/minimal.stdout.txt"
out_dir = ".artifacts/debug/minimal"
args = []
```
