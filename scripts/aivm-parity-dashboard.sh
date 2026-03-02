#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPORT_PATH="${1:-${ROOT_DIR}/Docs/AiVM-C-Parity-Status.md}"
TMP_DIR="${ROOT_DIR}/.tmp/aivm-parity-dashboard"
BUILD_DIR="${ROOT_DIR}/.tmp/aivm-c-build"
PARITY_CLI="${BUILD_DIR}/aivm_parity_cli"

mkdir -p "${TMP_DIR}"
mkdir -p "$(dirname "${REPORT_PATH}")"

cd "${ROOT_DIR}"

./scripts/bootstrap-golden-publish-fixtures.sh >/dev/null
cmake -S "${ROOT_DIR}/AiVM.C" -B "${BUILD_DIR}" >/dev/null
cmake --build "${BUILD_DIR}" --target aivm_parity_cli >/dev/null

GOLDEN_INPUTS=()
while IFS= read -r line; do
  GOLDEN_INPUTS+=("${line}")
done < <(find "${ROOT_DIR}/examples/golden" -maxdepth 1 -type f -name '*.in.aos' | sort)

if [[ ${#GOLDEN_INPUTS[@]} -eq 0 ]]; then
  echo "No golden input files found under examples/golden." >&2
  exit 2
fi

TOTAL=0
PASSED=0
FAILED=0

DETAILS_FILE="${TMP_DIR}/details.tsv"
: > "${DETAILS_FILE}"

for INPUT in "${GOLDEN_INPUTS[@]}"; do
  NAME="$(basename "${INPUT}" .in.aos)"
  LEFT_OUT="${TMP_DIR}/${NAME}.canonical.out"
  RIGHT_OUT="${TMP_DIR}/${NAME}.cvm.out"

  set +e
  ./tools/airun run "${INPUT}" >"${LEFT_OUT}" 2>&1
  LEFT_STATUS=$?
  ./tools/airun run "${INPUT}" --vm=c >"${RIGHT_OUT}" 2>&1
  RIGHT_STATUS=$?
  set -e

  TOTAL=$((TOTAL + 1))
  if [[ ${LEFT_STATUS} -eq ${RIGHT_STATUS} ]] && "${PARITY_CLI}" "${LEFT_OUT}" "${RIGHT_OUT}" >/dev/null 2>&1; then
    RESULT="PASS"
    PASSED=$((PASSED + 1))
  else
    RESULT="FAIL"
    FAILED=$((FAILED + 1))
  fi

  printf '%s\t%s\t%s\t%s\n' "${RESULT}" "${NAME}" "${LEFT_STATUS}" "${RIGHT_STATUS}" >> "${DETAILS_FILE}"
done

PERCENT="$(awk -v p="${PASSED}" -v t="${TOTAL}" 'BEGIN { if (t == 0) { print "0.00" } else { printf "%.2f", (p*100.0)/t } }')"
TS_UTC="$(date -u '+%Y-%m-%d %H:%M:%S UTC')"

{
  echo "# AiVM-C Parity Status"
  echo
  echo "Generated: ${TS_UTC}"
  echo
  echo "- Target suite: \`examples/golden/*.in.aos\`"
  echo "- Total targets: ${TOTAL}"
  echo "- Passing parity targets: ${PASSED}"
  echo "- Failing parity targets: ${FAILED}"
  echo "- Progress: ${PERCENT}%"
  echo
  echo "## Cases"
  echo
  echo "| Result | Case | Canonical Exit | C VM Exit |"
  echo "|---|---|---:|---:|"
  while IFS=$'\t' read -r RESULT NAME LEFT_STATUS RIGHT_STATUS; do
    echo "| ${RESULT} | ${NAME} | ${LEFT_STATUS} | ${RIGHT_STATUS} |"
  done < "${DETAILS_FILE}"
} > "${REPORT_PATH}"

echo "parity dashboard: ${PASSED}/${TOTAL} passing (${PERCENT}%)"
echo "report written: ${REPORT_PATH}"
