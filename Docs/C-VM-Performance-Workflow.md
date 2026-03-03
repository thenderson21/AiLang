# C VM Test, Profile, and Benchmark Workflow

Use these scripts to validate a C VM against the canonical runtime behavior and collect repeatable performance data.

## Scripts

- `./scripts/test-c-vm.sh`
- `./scripts/bench-c-vm.sh`
- `./scripts/profile-c-vm.sh`

## 1) Correctness testing

Compares candidate VM output to canonical `./tools/airun` for deterministic cases.

```bash
./scripts/test-c-vm.sh
```

Use a custom VM binary:

```bash
C_VM_BIN=./tools/airun-c ./scripts/test-c-vm.sh
```

Override how `run` is invoked:

```bash
C_VM_BIN=./tools/airun-c C_VM_RUN_ARGS="run --vm=bytecode" ./scripts/test-c-vm.sh
```

Optional custom case file format:

```text
# program|args
examples/hello.aos|
examples/echo.aos|hello
```

Run with custom case file:

```bash
CASE_FILE=/absolute/path/to/cases.txt ./scripts/test-c-vm.sh
```

## 2) Benchmarking

Runs deterministic benchmark programs and writes a timestamped report in `.artifacts/bench/c-vm/`.

```bash
./scripts/bench-c-vm.sh
```

Tune run counts:

```bash
ITERATIONS=50 WARMUP=10 ./scripts/bench-c-vm.sh
```

Use custom VM binary:

```bash
C_VM_BIN=./tools/airun-c ./scripts/bench-c-vm.sh
```

Optional custom benchmark case file format:

```text
# name|program|args
loop_compute|examples/bench/loop_compute.aos|
```

```bash
CASE_FILE=/absolute/path/to/bench-cases.txt ./scripts/bench-c-vm.sh
```

## 2.1) Regression gate (baseline-enforced)

Run the benchmark regression gate against frozen baseline values:

```bash
./scripts/aivm-bench-gate.sh
```

Optional tuning:

```bash
AIVM_BENCH_ITERATIONS=10 AIVM_BENCH_MAX_REGRESSION_PCT=5 ./scripts/aivm-bench-gate.sh
```

Inputs:

- Baseline file: `src/AiVM.Core/native/tests/compiler_runtime_bench_baseline.tsv`
- Current run: `./tools/airun bench --iterations <n> --human`
- Policy: fail if any baseline case is missing from current output or exceeds `max_regression_pct`.

## 3) Profiling

Profiles one program and writes artifacts in `.artifacts/profile/c-vm/<timestamp>/`.

```bash
./scripts/profile-c-vm.sh examples/bench/loop_compute.aos
```

Modes:

- `PROFILE_MODE=auto` (default): use `perf` if available, else `time`
- `PROFILE_MODE=perf`: force `perf stat`
- `PROFILE_MODE=time`: force `/usr/bin/time`

Examples:

```bash
PROFILE_MODE=time ./scripts/profile-c-vm.sh examples/bench/str_concat.aos
PROFILE_MODE=perf PERF_RUNS=10 ./scripts/profile-c-vm.sh examples/bench/map_create.aos
```

With VM args:

```bash
C_VM_BIN=./tools/airun-c C_VM_RUN_ARGS="run --vm=bytecode" ./scripts/profile-c-vm.sh examples/bench/loop_compute.aos
```

## 4) Memory leak gate

Runs repeated deterministic executions and enforces RSS-growth threshold.

```bash
./scripts/aivm-mem-leak-check.sh ./src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos 20
```

Optional threshold tuning (KB):

```bash
AIVM_LEAK_MAX_RSS_GROWTH_KB=2048 ./scripts/aivm-mem-leak-check.sh ./src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos 20
```

Report path can be overridden with `AIVM_LEAK_REPORT`.

## Notes

- Keep benchmark inputs deterministic and side-effect-free.
- For apples-to-apples runs, use the same machine, power mode, and background-load conditions.
- The C VM should match canonical output and exit codes before treating benchmark numbers as valid.
