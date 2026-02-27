#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_DIR="${ROOT_DIR}/.tmp/aivm-c-build"
BASELINE_FILE="${ROOT_DIR}/AiVM.C/tests/perf_baseline.env"
RUNS="${1:-20}"

if ! [[ "${RUNS}" =~ ^[0-9]+$ ]] || [[ "${RUNS}" -lt 1 ]]; then
  echo "runs must be positive integer, got: ${RUNS}" >&2
  exit 2
fi

if [[ ! -f "${BASELINE_FILE}" ]]; then
  echo "missing perf baseline file: ${BASELINE_FILE}" >&2
  exit 2
fi

# shellcheck source=/dev/null
source "${BASELINE_FILE}"

if [[ -z "${AIVM_TEST_VM_OPS_MEDIAN_MS_MAX:-}" ]]; then
  echo "baseline missing AIVM_TEST_VM_OPS_MEDIAN_MS_MAX" >&2
  exit 2
fi

cmake -S "${ROOT_DIR}/AiVM.C" -B "${BUILD_DIR}" >/dev/null
cmake --build "${BUILD_DIR}" --target aivm_test_vm_ops >/dev/null

python3 - "${BUILD_DIR}/aivm_test_vm_ops" "${RUNS}" "${AIVM_TEST_VM_OPS_MEDIAN_MS_MAX}" <<'PY'
import statistics
import subprocess
import sys
import time

binary = sys.argv[1]
runs = int(sys.argv[2])
max_median_ms = float(sys.argv[3])

samples = []
for i in range(runs):
    t0 = time.perf_counter()
    proc = subprocess.run([binary], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if proc.returncode != 0:
        raise SystemExit(f"perf smoke target failed at iteration {i+1} with code {proc.returncode}")
    samples.append((time.perf_counter() - t0) * 1000.0)

median_ms = statistics.median(samples)
avg_ms = statistics.mean(samples)
max_ms = max(samples)
min_ms = min(samples)

print(f"aivm_test_vm_ops.runs={runs}")
print(f"aivm_test_vm_ops.min_ms={min_ms:.3f}")
print(f"aivm_test_vm_ops.median_ms={median_ms:.3f}")
print(f"aivm_test_vm_ops.avg_ms={avg_ms:.3f}")
print(f"aivm_test_vm_ops.max_ms={max_ms:.3f}")
print(f"aivm_test_vm_ops.median_limit_ms={max_median_ms:.3f}")

if median_ms > max_median_ms:
    raise SystemExit(f"median_ms {median_ms:.3f} exceeded limit {max_median_ms:.3f}")
PY
