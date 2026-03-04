#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BASELINE_FILE="${AIVM_BENCH_BASELINE_FILE:-${ROOT_DIR}/src/AiVM.Core/native/tests/compiler_runtime_bench_baseline.tsv}"
ITERATIONS="${AIVM_BENCH_ITERATIONS:-10}"
MAX_REGRESSION_PCT="${AIVM_BENCH_MAX_REGRESSION_PCT:-5}"
TMP_DIR="${ROOT_DIR}/.tmp/aivm-bench-gate"
BENCH_OUT="${TMP_DIR}/bench.out"
CURRENT_TSV="${TMP_DIR}/bench-current.tsv"

mkdir -p "${TMP_DIR}"
cd "${ROOT_DIR}"

if [[ ! -x "${ROOT_DIR}/tools/airun" ]]; then
  echo "missing runtime: ./tools/airun (run ./scripts/build-airun.sh first)" >&2
  exit 2
fi

if [[ ! -f "${BASELINE_FILE}" ]]; then
  echo "missing benchmark baseline: ${BASELINE_FILE}" >&2
  exit 2
fi

if ! [[ "${ITERATIONS}" =~ ^[0-9]+$ ]] || [[ "${ITERATIONS}" -lt 1 ]]; then
  echo "AIVM_BENCH_ITERATIONS must be a positive integer, got: ${ITERATIONS}" >&2
  exit 2
fi

if ! [[ "${MAX_REGRESSION_PCT}" =~ ^[0-9]+([.][0-9]+)?$ ]]; then
  echo "AIVM_BENCH_MAX_REGRESSION_PCT must be numeric, got: ${MAX_REGRESSION_PCT}" >&2
  exit 2
fi

./tools/airun bench --iterations "${ITERATIONS}" --human > "${BENCH_OUT}" 2>&1
awk 'NR>1 && $2 == "ok" {print $1 "\t" $4}' "${BENCH_OUT}" > "${CURRENT_TSV}"

MISSING_COUNT="$(
  awk 'BEGIN{c=0} !/^#/ && NF>=2 {print $1}' "${BASELINE_FILE}" | while read -r name; do
    if ! awk -v target="${name}" '$1 == target { found = 1; exit 0 } END { exit(found ? 0 : 1) }' "${CURRENT_TSV}"; then
      echo 1
    fi
  done | wc -l | tr -d ' '
)"

REGRESSION_COUNT="$(
  awk -v max_pct="${MAX_REGRESSION_PCT}" '
    BEGIN {
      FS = "\t";
      while ((getline < ARGV[1]) > 0) {
        if ($0 ~ /^#/ || NF < 2) { continue; }
        base[$1] = $2 + 0;
      }
      close(ARGV[1]);
      count = 0;
      while ((getline < ARGV[2]) > 0) {
        if (NF < 2) { continue; }
        name = $1;
        current = $2 + 0;
        if (!(name in base)) { continue; }
        limit = base[name] * (1.0 + (max_pct / 100.0));
        if (current > limit) { count += 1; }
      }
      close(ARGV[2]);
      print count;
    }
  ' "${BASELINE_FILE}" "${CURRENT_TSV}"
)"

echo "benchmark gate: baseline=${BASELINE_FILE##${ROOT_DIR}/} iterations=${ITERATIONS} max_regression_pct=${MAX_REGRESSION_PCT}"
echo "benchmark gate: missing=${MISSING_COUNT} regressions=${REGRESSION_COUNT}"

if [[ "${MISSING_COUNT}" != "0" || "${REGRESSION_COUNT}" != "0" ]]; then
  echo "benchmark gate failed; see ${BENCH_OUT} and ${CURRENT_TSV}" >&2
  exit 1
fi

echo "benchmark gate passed"
