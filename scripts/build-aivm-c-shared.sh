#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PREFERRED_C_SOURCE_DIR="${ROOT_DIR}/src/AiVM.Core/native"
AIVM_C_SOURCE_DIR="${AIVM_C_SOURCE_DIR:-${PREFERRED_C_SOURCE_DIR}}"
BUILD_SUFFIX="native"
BUILD_DIR="${ROOT_DIR}/.tmp/aivm-c-build-shared-${BUILD_SUFFIX}"

if [[ -f "${AIVM_C_SOURCE_DIR}/CMakePresets.json" ]]; then
  pushd "${AIVM_C_SOURCE_DIR}" >/dev/null
  cmake --preset aivm-native-shared-unix --fresh >/dev/null
  cmake --build --preset aivm-native-shared-unix-build >/dev/null
  popd >/dev/null
else
  cmake -S "${AIVM_C_SOURCE_DIR}" -B "${BUILD_DIR}" -DAIVM_BUILD_SHARED=ON
  cmake --build "${BUILD_DIR}"
fi

if [[ -f "${BUILD_DIR}/libaivm_core_shared.dylib" ]]; then
  printf '%s\n' "${BUILD_DIR}/libaivm_core_shared.dylib"
  exit 0
fi
if [[ -f "${BUILD_DIR}/libaivm_core_shared.so" ]]; then
  printf '%s\n' "${BUILD_DIR}/libaivm_core_shared.so"
  exit 0
fi
if [[ -f "${BUILD_DIR}/aivm_core_shared.dll" ]]; then
  printf '%s\n' "${BUILD_DIR}/aivm_core_shared.dll"
  exit 0
fi

echo "shared library was not produced in ${BUILD_DIR}" >&2
exit 1
