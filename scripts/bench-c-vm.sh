#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

C_VM_BIN="${C_VM_BIN:-./tools/airun}"
C_VM_RUN_ARGS="${C_VM_RUN_ARGS:-run}"
ITERATIONS="${ITERATIONS:-30}"
WARMUP="${WARMUP:-5}"
CASE_FILE="${CASE_FILE:-}"

if ! [[ "${ITERATIONS}" =~ ^[0-9]+$ ]] || [[ "${ITERATIONS}" -lt 1 ]]; then
  echo "ITERATIONS must be a positive integer, got: ${ITERATIONS}" >&2
  exit 1
fi
if ! [[ "${WARMUP}" =~ ^[0-9]+$ ]]; then
  echo "WARMUP must be a non-negative integer, got: ${WARMUP}" >&2
  exit 1
fi

TMP_CASES=""
if [[ -z "${CASE_FILE}" ]]; then
  TMP_CASES="$(mktemp -t c-vm-bench-cases-XXXXXX.txt)"
  CASE_FILE="${TMP_CASES}"
  cat > "${CASE_FILE}" <<'EOF'
# name|program|args
loop_compute|examples/bench/loop_compute.aos|
str_concat|examples/bench/str_concat.aos|
map_create|examples/bench/map_create.aos|
http_handler|examples/bench/http_handler.aos|
lifecycle|examples/bench/lifecycle_run.aos|
EOF
fi

if [[ ! -f "${CASE_FILE}" ]]; then
  echo "CASE_FILE does not exist: ${CASE_FILE}" >&2
  exit 1
fi

mkdir -p .artifacts/bench/c-vm
STAMP="$(date '+%Y%m%d-%H%M%S')"
REPORT_PATH=".artifacts/bench/c-vm/${STAMP}.txt"

cleanup() {
  rm -f "${TMP_CASES}"
}
trap cleanup EXIT

IFS=' ' read -r -a C_VM_RUN_ARR <<< "${C_VM_RUN_ARGS}"

python3 - "${C_VM_BIN}" "${ITERATIONS}" "${WARMUP}" "${CASE_FILE}" "${C_VM_RUN_ARR[@]}" <<'PY' | tee "${REPORT_PATH}"
import os
import shlex
import statistics
import subprocess
import sys
import time

vm_bin = sys.argv[1]
iterations = int(sys.argv[2])
warmup = int(sys.argv[3])
case_file = sys.argv[4]
run_args = sys.argv[5:]

print(f"vm_bin={vm_bin}")
print(f"iterations={iterations}")
print(f"warmup={warmup}")

for raw in open(case_file, "r", encoding="utf-8"):
    line = raw.strip()
    if not line or line.startswith("#"):
        continue
    parts = line.split("|")
    if len(parts) < 2:
        raise SystemExit(f"invalid case line: {line}")
    name = parts[0].strip()
    program = parts[1].strip()
    args_str = parts[2].strip() if len(parts) > 2 else ""

    if not os.path.isfile(program):
        raise SystemExit(f"program does not exist: {program}")

    case_args = shlex.split(args_str) if args_str else []
    cmd = [vm_bin] + run_args + [program] + case_args

    for _ in range(warmup):
        proc = subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if proc.returncode != 0:
            raise SystemExit(f"warmup failed for case={name}, code={proc.returncode}")

    times = []
    for i in range(iterations):
        t0 = time.perf_counter()
        proc = subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if proc.returncode != 0:
            raise SystemExit(f"bench failed for case={name}, iter={i+1}, code={proc.returncode}")
        times.append((time.perf_counter() - t0) * 1000.0)

    times.sort()
    p90_idx = max(0, int(len(times) * 0.90) - 1)
    print(f"case={name} program={program}")
    print(f"  avg_ms={statistics.mean(times):.3f}")
    print(f"  median_ms={statistics.median(times):.3f}")
    print(f"  p90_ms={times[p90_idx]:.3f}")
    print(f"  min_ms={times[0]:.3f}")
    print(f"  max_ms={times[-1]:.3f}")
PY

echo "report=${REPORT_PATH}"

