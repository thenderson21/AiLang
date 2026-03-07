#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PREFERRED_C_SOURCE_DIR="${ROOT_DIR}/src/AiVM.Core/native"
AIVM_C_SOURCE_DIR="${AIVM_C_SOURCE_DIR:-${PREFERRED_C_SOURCE_DIR}}"
PARITY_REPORT="${AIVM_PARITY_REPORT:-${ROOT_DIR}/.tmp/aivm-dualrun-manifest/report.txt}"
SHARED_FLAG="-DAIVM_BUILD_SHARED=OFF"
if [[ "${AIVM_BUILD_SHARED:-0}" == "1" ]]; then
  SHARED_FLAG="-DAIVM_BUILD_SHARED=ON"
fi

if [[ ! -x "${ROOT_DIR}/tools/airun" ]]; then
  ./scripts/build-airun.sh >/dev/null
fi
if [[ ! -x "${ROOT_DIR}/tools/airun" ]]; then
  echo "missing runtime: ${ROOT_DIR}/tools/airun" >&2
  exit 2
fi

if [[ ! -f "${AIVM_C_SOURCE_DIR}/CMakePresets.json" ]]; then
  echo "missing ${AIVM_C_SOURCE_DIR}/CMakePresets.json; native test flow requires presets" >&2
  exit 2
fi

pushd "${AIVM_C_SOURCE_DIR}" >/dev/null
TEST_PRESET="${AIVM_CTEST_PRESET:-aivm-native-unix-test}"
cmake --preset aivm-native-unix --fresh "${SHARED_FLAG}"
cmake --build --preset aivm-native-unix-build
ctest --preset "${TEST_PRESET}"
popd >/dev/null

mkdir -p "$(dirname "${PARITY_REPORT}")"
{
  echo "task edge parity checks"
  echo "summary|owned_by=ctest|tests=aivm_test_task_edge_parity,aivm_test_airun_smoke,aivm_test_debug_memory_smoke"
} > "${PARITY_REPORT}"

if [[ "${AIVM_MEM_LEAK_GATE:-0}" == "1" ]]; then
  leak_iterations="${AIVM_LEAK_CHECK_ITERATIONS:-50}"
  leak_target="${AIVM_LEAK_CHECK_TARGET:-${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos}"
  AIVM_LEAK_MAX_GROWTH_KB="${AIVM_LEAK_MAX_GROWTH_KB:-2048}" \
    "${ROOT_DIR}/scripts/aivm-mem-leak-check.sh" "${leak_target}" "${leak_iterations}" >/dev/null
fi

if [[ "${AIVM_PERF_SMOKE:-0}" == "1" ]]; then
  "${ROOT_DIR}/scripts/aivm-c-perf-smoke.sh" "${AIVM_PERF_RUNS:-10}"
fi
