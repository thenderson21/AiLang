#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PREFERRED_C_SOURCE_DIR="${ROOT_DIR}/src/AiVM.Core/native"
AIVM_C_SOURCE_DIR="${AIVM_C_SOURCE_DIR:-${PREFERRED_C_SOURCE_DIR}}"
BUILD_SUFFIX="native"
BUILD_DIR="${AIVM_C_BUILD_DIR:-${ROOT_DIR}/.tmp/aivm-c-build-${BUILD_SUFFIX}}"
PARITY_REPORT="${AIVM_PARITY_REPORT:-${ROOT_DIR}/.tmp/aivm-dualrun-manifest/report.txt}"
PARITY_MANIFEST="${AIVM_PARITY_MANIFEST:-${AIVM_C_SOURCE_DIR}/tests/parity_commands_ci.txt}"
SHARED_FLAG="-DAIVM_BUILD_SHARED=OFF"
if [[ "${AIVM_BUILD_SHARED:-0}" == "1" ]]; then
  SHARED_FLAG="-DAIVM_BUILD_SHARED=ON"
fi

cmake -S "${AIVM_C_SOURCE_DIR}" -B "${BUILD_DIR}" "${SHARED_FLAG}"
cmake --build "${BUILD_DIR}"
ctest --test-dir "${BUILD_DIR}" --output-on-failure

if [[ -x "${ROOT_DIR}/tools/airun" ]]; then
  "${ROOT_DIR}/tools/airun" run "${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos" --vm=c >/dev/null
  TMP_NATIVE_PUBLISH_DIR="${ROOT_DIR}/.tmp/aivm-c-native-publish-smoke"
  rm -rf "${TMP_NATIVE_PUBLISH_DIR}"
  "${ROOT_DIR}/tools/airun" publish "${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos" --out "${TMP_NATIVE_PUBLISH_DIR}" >/dev/null
  if [[ ! -f "${TMP_NATIVE_PUBLISH_DIR}/app.aibc1" ]]; then
    echo "native publish smoke failed: app.aibc1 was not produced" >&2
    exit 1
  fi
  if [[ ! -x "${TMP_NATIVE_PUBLISH_DIR}/vm_c_execute_src_main_params" ]]; then
    echo "native publish smoke failed: app executable is missing" >&2
    exit 1
  fi
  if [[ -e "${TMP_NATIVE_PUBLISH_DIR}/run.sh" || -e "${TMP_NATIVE_PUBLISH_DIR}/run.ps1" || -e "${TMP_NATIVE_PUBLISH_DIR}/run.cmd" ]]; then
    echo "native publish smoke failed: launcher files should not be generated" >&2
    exit 1
  fi
  "${TMP_NATIVE_PUBLISH_DIR}/vm_c_execute_src_main_params" >/dev/null
  if [[ $? -ne 0 ]]; then
    echo "native publish smoke failed: direct app executable did not run" >&2
    exit 1
  fi

  case "$(uname -s)" in
    Darwin) _plat="osx" ;;
    Linux) _plat="linux" ;;
    *) _plat="" ;;
  esac
  case "$(uname -m)" in
    arm64|aarch64) _arch="arm64" ;;
    x86_64|amd64) _arch="x64" ;;
    *) _arch="" ;;
  esac
  if [[ -n "${_plat}" && -n "${_arch}" ]]; then
    HOST_RID="${_plat}-${_arch}"
    TMP_NATIVE_PROJECT_DIR="${ROOT_DIR}/.tmp/aivm-c-native-project-target"
    TMP_NATIVE_PROJECT_OUT="${ROOT_DIR}/.tmp/aivm-c-native-project-target-out"
    rm -rf "${TMP_NATIVE_PROJECT_DIR}" "${TMP_NATIVE_PROJECT_OUT}"
    mkdir -p "${TMP_NATIVE_PROJECT_DIR}"
    cat > "${TMP_NATIVE_PROJECT_DIR}/project.aiproj" <<EOF
Program#p1 {
  Project#proj1(name="projtarget" entryFile="main.aos" entryExport="main" publishTarget="${HOST_RID}")
}
EOF
    cat > "${TMP_NATIVE_PROJECT_DIR}/main.aos" <<'EOF'
Bytecode#bc1(magic="AIBC" format="AiBC1" version=2 flags=0) {
  Func#f1(name=main params="argv" locals="") {
    Inst#i1(op=HALT)
  }
}
EOF
    "${ROOT_DIR}/tools/airun" publish "${TMP_NATIVE_PROJECT_DIR}" --out "${TMP_NATIVE_PROJECT_OUT}" >/dev/null
    if [[ ! -x "${TMP_NATIVE_PROJECT_OUT}/projtarget" ]]; then
      echo "native publish target-from-manifest failed: projtarget executable missing" >&2
      exit 1
    fi
  fi
fi

mkdir -p "$(dirname "${PARITY_REPORT}")"
printf 'parity manifest skipped: source-mode dualrun removed in C-only runtime cutover\n' > "${PARITY_REPORT}"

if [[ "${AIVM_MEM_LEAK_GATE:-0}" == "1" ]]; then
  leak_iterations="${AIVM_LEAK_CHECK_ITERATIONS:-50}"
  leak_target="${AIVM_LEAK_CHECK_TARGET:-${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos}"
  AIVM_LEAK_MAX_GROWTH_KB="${AIVM_LEAK_MAX_GROWTH_KB:-2048}" \
    "${ROOT_DIR}/scripts/aivm-mem-leak-check.sh" "${leak_target}" "${leak_iterations}" >/dev/null
fi

if [[ "${AIVM_PERF_SMOKE:-0}" == "1" ]]; then
  "${ROOT_DIR}/scripts/aivm-c-perf-smoke.sh" "${AIVM_PERF_RUNS:-10}"
fi
