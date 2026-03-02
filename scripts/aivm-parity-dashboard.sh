#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PREFERRED_C_SOURCE_DIR="${ROOT_DIR}/src/AiVM.Core/native"
AIVM_C_SOURCE_DIR="${AIVM_C_SOURCE_DIR:-${PREFERRED_C_SOURCE_DIR}}"
AIVM_C_TESTS_DIR="${AIVM_C_TESTS_DIR:-${AIVM_C_SOURCE_DIR}/tests}"
REPORT_PATH="${1:-${ROOT_DIR}/Docs/AiVM-C-Parity-Status.md}"
TMP_DIR="${ROOT_DIR}/.tmp/aivm-parity-dashboard"
BUILD_SUFFIX="native"
BUILD_DIR="${ROOT_DIR}/.tmp/aivm-c-build-${BUILD_SUFFIX}"
BUILD_OUTPUT_DIR="${BUILD_DIR}"
PARITY_CLI="${BUILD_DIR}/aivm_parity_cli"
MODE="${AIVM_PARITY_DASHBOARD_MODE:-auto}"
BRIDGE_LIB="${AIVM_C_BRIDGE_LIB:-}"
BRIDGE_ENABLED=0
MODE_USED="gate"
RUN_TESTS="${AIVM_DOD_RUN_TESTS:-1}"
RUN_BENCH="${AIVM_DOD_RUN_BENCH:-1}"

mkdir -p "${TMP_DIR}"
mkdir -p "$(dirname "${REPORT_PATH}")"
cd "${ROOT_DIR}"

run_c_mode() {
  local input="$1"
  local output="$2"
  if [[ ${BRIDGE_ENABLED} -eq 1 ]]; then
    AIVM_C_BRIDGE_EXECUTE=1 AIVM_C_BRIDGE_LIB="${BRIDGE_LIB}" ./tools/airun run "${input}" --vm=c >"${output}" 2>&1
  else
    ./tools/airun run "${input}" --vm=c >"${output}" 2>&1
  fi
}

status_word() {
  local status="$1"
  if [[ "${status}" == "PASS" ]]; then
    printf "PASS"
  elif [[ "${status}" == "FAIL" ]]; then
    printf "FAIL"
  else
    printf "PENDING"
  fi
}

./scripts/bootstrap-golden-publish-fixtures.sh >/dev/null
cmake -S "${AIVM_C_SOURCE_DIR}" -B "${BUILD_DIR}" >/dev/null
cmake --build "${BUILD_DIR}" --target aivm_parity_cli >/dev/null

if [[ "${MODE}" != "gate" ]]; then
  if [[ -z "${BRIDGE_LIB}" ]]; then
    set +e
    BRIDGE_LIB="$(./scripts/build-aivm-c-shared.sh 2>/dev/null | tail -n1)"
    BUILD_SHARED_STATUS=$?
    set -e
    if [[ ${BUILD_SHARED_STATUS} -ne 0 ]]; then
      BRIDGE_LIB=""
    fi
  fi
  if [[ -n "${BRIDGE_LIB}" && -f "${BRIDGE_LIB}" ]]; then
    BRIDGE_ENABLED=1
    MODE_USED="execute"
  elif [[ "${MODE}" == "execute" ]]; then
    echo "AIVM parity dashboard execute mode requested, but bridge library was not available." >&2
    exit 2
  fi
fi

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

for input in "${GOLDEN_INPUTS[@]}"; do
  name="$(basename "${input}" .in.aos)"
  left_out="${TMP_DIR}/${name}.canonical.out"
  right_out="${TMP_DIR}/${name}.cvm.out"

  set +e
  ./tools/airun run "${input}" >"${left_out}" 2>&1
  left_status=$?
  run_c_mode "${input}" "${right_out}"
  right_status=$?
  set -e

  TOTAL=$((TOTAL + 1))
  if [[ ${left_status} -eq ${right_status} ]] && "${PARITY_CLI}" "${left_out}" "${right_out}" >/dev/null 2>&1; then
    result="PASS"
    PASSED=$((PASSED + 1))
  else
    result="FAIL"
    FAILED=$((FAILED + 1))
  fi
  printf '%s\t%s\t%s\t%s\n' "${result}" "${name}" "${left_status}" "${right_status}" >> "${DETAILS_FILE}"
done

PARITY_PERCENT="$(awk -v p="${PASSED}" -v t="${TOTAL}" 'BEGIN { if (t == 0) { print "0.00" } else { printf "%.2f", (p*100.0)/t } }')"

# Behavioral parity gate and required entrypoint sub-gates.
ENTRY_SOURCE_STATUS="FAIL"
ENTRY_SOURCE_DETAILS="backed by canonical golden corpus parity"
if [[ ${PASSED} -eq ${TOTAL} ]]; then
  ENTRY_SOURCE_STATUS="PASS"
fi
ENTRY_BYTECODE_STATUS="PENDING"
ENTRY_BYTECODE_DETAILS="execute-mode check not available (bridge not enabled)"
ENTRY_BUNDLE_STATUS="PENDING"
ENTRY_BUNDLE_DETAILS="execute-mode check not available (bridge not enabled)"
ENTRY_SERVE_STATUS="PENDING"
ENTRY_SERVE_DETAILS="c serve entrypoint check not run"

if [[ ${BRIDGE_ENABLED} -eq 1 ]]; then
  set +e
  AIVM_C_BRIDGE_EXECUTE=1 AIVM_C_BRIDGE_LIB="${BRIDGE_LIB}" ./tools/airun run "${AIVM_C_TESTS_DIR}/parity_cases/vm_c_execute_src_main_params.aos" --vm=c > "${TMP_DIR}/entry-bytecode.out" 2>&1
  entry_bytecode_rc=$?
  AIVM_C_BRIDGE_EXECUTE=1 AIVM_C_BRIDGE_LIB="${BRIDGE_LIB}" ./tools/airun run examples/golden/publishcases/include_success/app_include_ok.aibundle --vm=c > "${TMP_DIR}/entry-bundle.out" 2>&1
  entry_bundle_rc=$?
  AIVM_C_BRIDGE_EXECUTE=1 AIVM_C_BRIDGE_LIB="${BRIDGE_LIB}" ./tools/airun serve examples/golden/http/health_app.aos --vm=c --port 8089 > "${TMP_DIR}/entry-serve.out" 2>&1
  entry_serve_rc=$?
  set -e

  if [[ ${entry_bytecode_rc} -eq 0 ]]; then
    ENTRY_BYTECODE_STATUS="PASS"
    ENTRY_BYTECODE_DETAILS="vm=c run Bytecode source executes through bridge"
  else
    ENTRY_BYTECODE_STATUS="FAIL"
    ENTRY_BYTECODE_DETAILS="vm=c run Bytecode source failed (exit=${entry_bytecode_rc})"
  fi

  if [[ ${entry_bundle_rc} -eq 0 ]]; then
    ENTRY_BUNDLE_STATUS="PASS"
    ENTRY_BUNDLE_DETAILS="vm=c run .aibundle executes through bridge"
  else
    ENTRY_BUNDLE_STATUS="FAIL"
    ENTRY_BUNDLE_DETAILS="vm=c run .aibundle failed (exit=${entry_bundle_rc})"
  fi

  if [[ ${entry_serve_rc} -eq 1 ]] && rg -q 'code=DEV008' "${TMP_DIR}/entry-serve.out"; then
    ENTRY_SERVE_STATUS="PASS"
    ENTRY_SERVE_DETAILS="vm=c serve deterministically reports DEV008 (not linked)"
  else
    ENTRY_SERVE_STATUS="FAIL"
    ENTRY_SERVE_DETAILS="vm=c serve behavior deviated (exit=${entry_serve_rc})"
  fi
fi

BEHAVIORAL_GATE_STATUS="FAIL"
if [[ ${PASSED} -eq ${TOTAL} &&
      "${ENTRY_SOURCE_STATUS}" == "PASS" &&
      "${ENTRY_BYTECODE_STATUS}" == "PASS" &&
      "${ENTRY_BUNDLE_STATUS}" == "PASS" &&
      "${ENTRY_SERVE_STATUS}" == "PASS" ]]; then
  BEHAVIORAL_GATE_STATUS="PASS"
fi

# Zero-C# gate.
TRACKED_CS_COUNT="$(git ls-files '*.cs' '*.csproj' '*.sln' '*.slnx' | wc -l | tr -d ' ')"
DOTNET_REF_COUNT="$( (rg -n '\bdotnet\b' .github/workflows scripts 2>/dev/null || true) | wc -l | tr -d ' ')"
ZERO_CSHARP_STATUS="FAIL"
if [[ "${TRACKED_CS_COUNT}" == "0" && "${DOTNET_REF_COUNT}" == "0" ]]; then
  ZERO_CSHARP_STATUS="PASS"
fi

# Test coverage gate.
TEST_GATE_STATUS="PENDING"
TEST_AIVM_C_STATUS="not-run"
TEST_FULL_STATUS="not-run"
DETERMINISM_STATUS="not-run"
if [[ "${RUN_TESTS}" == "1" ]]; then
  set +e
  ./scripts/test-aivm-c.sh > "${TMP_DIR}/test-aivm-c.log" 2>&1
  t1=$?
  ./scripts/test.sh > "${TMP_DIR}/test-full.log" 2>&1
  t2=$?
  ctest --test-dir "${BUILD_DIR}" -R aivm_test_vm_determinism > "${TMP_DIR}/test-determinism.log" 2>&1
  t3=$?
  set -e
  if [[ ${t1} -eq 0 ]]; then TEST_AIVM_C_STATUS="pass"; else TEST_AIVM_C_STATUS="fail"; fi
  if [[ ${t2} -eq 0 ]]; then TEST_FULL_STATUS="pass"; else TEST_FULL_STATUS="fail"; fi
  if [[ ${t3} -eq 0 ]]; then DETERMINISM_STATUS="pass"; else DETERMINISM_STATUS="fail"; fi
  if [[ ${t1} -eq 0 && ${t2} -eq 0 && ${t3} -eq 0 ]]; then
    TEST_GATE_STATUS="PASS"
  else
    TEST_GATE_STATUS="FAIL"
  fi
fi

# Benchmark gate.
BENCH_GATE_STATUS="PENDING"
BENCH_BASELINE_FILE="${AIVM_C_TESTS_DIR}/compiler_runtime_bench_baseline.tsv"
BENCH_RUN_STATUS="not-run"
BENCH_BASELINE_STATUS="missing"
BENCH_THRESHOLD_STATUS="not-evaluated"
BENCH_REGRESSION_COUNT=0
BENCH_BASELINE_MISSING_COUNT=0
BENCH_ALLOWED_REGRESSION_PCT="${AIVM_BENCH_MAX_REGRESSION_PCT:-5}"
if [[ "${RUN_BENCH}" == "1" ]]; then
  set +e
  ./tools/airun bench --iterations 10 --human > "${TMP_DIR}/bench.out" 2>&1
  bench_rc=$?
  set -e
  if [[ ${bench_rc} -eq 0 ]]; then
    BENCH_RUN_STATUS="pass"
  else
    BENCH_RUN_STATUS="fail"
  fi

  if [[ -f "${BENCH_BASELINE_FILE}" ]]; then
    BENCH_BASELINE_STATUS="present"
    invalid_baseline_count="$(awk 'BEGIN{c=0} !/^#/ && NF>=2 {if ($2 <= 0) c++} END{print c}' "${BENCH_BASELINE_FILE}")"
    if [[ "${invalid_baseline_count}" == "0" && "${BENCH_RUN_STATUS}" == "pass" ]]; then
      awk 'NR>1 && $2 == "ok" {print $1 "\t" $4}' "${TMP_DIR}/bench.out" > "${TMP_DIR}/bench-current.tsv"
      BENCH_BASELINE_MISSING_COUNT="$(awk 'BEGIN{c=0} !/^#/ && NF>=2 {print $1}' "${BENCH_BASELINE_FILE}" | while read -r name; do if ! rg -q "^${name}[[:space:]]" "${TMP_DIR}/bench-current.tsv"; then echo 1; fi; done | wc -l | tr -d ' ')"
      BENCH_REGRESSION_COUNT="$(
        awk -v max_pct="${BENCH_ALLOWED_REGRESSION_PCT}" '
          BEGIN {
            FS="\t";
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
        ' "${BENCH_BASELINE_FILE}" "${TMP_DIR}/bench-current.tsv"
      )"
      if [[ "${BENCH_BASELINE_MISSING_COUNT}" == "0" && "${BENCH_REGRESSION_COUNT}" == "0" ]]; then
        BENCH_THRESHOLD_STATUS="within-threshold"
        BENCH_GATE_STATUS="PASS"
      else
        BENCH_THRESHOLD_STATUS="regression-or-missing"
        BENCH_GATE_STATUS="FAIL"
      fi
    else
      BENCH_THRESHOLD_STATUS="baseline-not-calibrated"
      BENCH_GATE_STATUS="FAIL"
    fi
  else
    BENCH_BASELINE_STATUS="missing"
    BENCH_THRESHOLD_STATUS="baseline-not-found"
    BENCH_GATE_STATUS="FAIL"
  fi
fi

# Sample completion gate.
SAMPLE_GATE_STATUS="FAIL"
SAMPLE_MANIFEST="${ROOT_DIR}/Docs/Sample-Completion-Manifest.md"
sample_total="$(find "${ROOT_DIR}/samples" -mindepth 1 -maxdepth 2 -name project.aiproj | wc -l | tr -d ' ')"
sample_complete=0
if [[ -f "${SAMPLE_MANIFEST}" ]]; then
  sample_complete="$( (rg -n '\|\s*`samples/.*\|\s*pass\s*\|\s*pass\s*\|\s*pass\s*\|\s*pass\s*\|\s*COMPLETE\s*\|' "${SAMPLE_MANIFEST}" || true) | wc -l | tr -d ' ')"
fi
if [[ "${sample_total}" != "0" && "${sample_complete}" == "${sample_total}" ]]; then
  SAMPLE_GATE_STATUS="PASS"
fi

# Memory/GC gate.
MEMORY_GATE_STATUS="FAIL"
RC_TEST_PRESENT="no"
CYCLE_TEST_PRESENT="no"
LEAK_SCRIPT_PRESENT="no"
PROFILE_SCRIPT_PRESENT="no"
if [[ -f "${AIVM_C_TESTS_DIR}/test_memory_rc.c" ]]; then RC_TEST_PRESENT="yes"; fi
if [[ -f "${AIVM_C_TESTS_DIR}/test_memory_cycle.c" ]]; then CYCLE_TEST_PRESENT="yes"; fi
if [[ -x "${ROOT_DIR}/scripts/aivm-mem-leak-check.sh" ]]; then LEAK_SCRIPT_PRESENT="yes"; fi
if [[ -x "${ROOT_DIR}/scripts/aivm-mem-profile.sh" ]]; then PROFILE_SCRIPT_PRESENT="yes"; fi
if [[ "${RC_TEST_PRESENT}" == "yes" &&
      "${CYCLE_TEST_PRESENT}" == "yes" &&
      "${LEAK_SCRIPT_PRESENT}" == "yes" &&
      "${PROFILE_SCRIPT_PRESENT}" == "yes" ]]; then
  MEMORY_GATE_STATUS="PASS"
fi

OVERALL_STATUS="FAIL"
if [[ "${BEHAVIORAL_GATE_STATUS}" == "PASS" &&
      "${ZERO_CSHARP_STATUS}" == "PASS" &&
      "${TEST_GATE_STATUS}" == "PASS" &&
      "${BENCH_GATE_STATUS}" == "PASS" &&
      "${SAMPLE_GATE_STATUS}" == "PASS" &&
      "${MEMORY_GATE_STATUS}" == "PASS" ]]; then
  OVERALL_STATUS="PASS"
fi

TS_UTC="$(date -u '+%Y-%m-%d %H:%M:%S UTC')"
{
  echo "# AiLang Zero-C# DoD Dashboard"
  echo
  echo "Generated: ${TS_UTC}"
  echo
  echo "Overall status: **${OVERALL_STATUS}**"
  echo
  echo "## Gates"
  echo
  echo "| Gate | Status | Details |"
  echo "|---|---|---|"
  echo "| Behavioral parity | ${BEHAVIORAL_GATE_STATUS} | ${PASSED}/${TOTAL} (${PARITY_PERCENT}%) with mode=${MODE_USED} |"
  echo "| Zero-C# | ${ZERO_CSHARP_STATUS} | tracked_csharp=${TRACKED_CS_COUNT}, dotnet_refs_in_ci_scripts=${DOTNET_REF_COUNT} |"
  echo "| Test coverage | ${TEST_GATE_STATUS} | test-aivm-c=${TEST_AIVM_C_STATUS}, test.sh=${TEST_FULL_STATUS}, determinism=${DETERMINISM_STATUS} |"
  echo "| Benchmark | ${BENCH_GATE_STATUS} | bench_run=${BENCH_RUN_STATUS}, baseline=${BENCH_BASELINE_STATUS}, threshold=${BENCH_THRESHOLD_STATUS}, regressions=${BENCH_REGRESSION_COUNT}, missing=${BENCH_BASELINE_MISSING_COUNT}, max_pct=${BENCH_ALLOWED_REGRESSION_PCT} |"
  echo "| Samples completion | ${SAMPLE_GATE_STATUS} | complete=${sample_complete}/${sample_total} (manifest=${SAMPLE_MANIFEST##${ROOT_DIR}/}) |"
  echo "| Memory/GC | ${MEMORY_GATE_STATUS} | rc_test=${RC_TEST_PRESENT}, cycle_test=${CYCLE_TEST_PRESENT}, leak_script=${LEAK_SCRIPT_PRESENT}, profile_script=${PROFILE_SCRIPT_PRESENT} |"
  echo
  echo "## Behavioral Sub-Gates"
  echo
  echo "| Entrypoint | Status | Details |"
  echo "|---|---|---|"
  echo "| run source | ${ENTRY_SOURCE_STATUS} | ${ENTRY_SOURCE_DETAILS} |"
  echo "| embedded bytecode | ${ENTRY_BYTECODE_STATUS} | ${ENTRY_BYTECODE_DETAILS} |"
  echo "| embedded bundle | ${ENTRY_BUNDLE_STATUS} | ${ENTRY_BUNDLE_DETAILS} |"
  echo "| serve | ${ENTRY_SERVE_STATUS} | ${ENTRY_SERVE_DETAILS} |"
  echo
  echo "## Behavioral Cases"
  echo
  echo "| Result | Case | Canonical Exit | C VM Exit |"
  echo "|---|---|---:|---:|"
  while IFS=$'\t' read -r result name left_status right_status; do
    echo "| ${result} | ${name} | ${left_status} | ${right_status} |"
  done < "${DETAILS_FILE}"
} > "${REPORT_PATH}"

echo "parity dashboard: ${PASSED}/${TOTAL} passing (${PARITY_PERCENT}%)"
echo "overall DoD status: ${OVERALL_STATUS}"
echo "report written: ${REPORT_PATH}"
