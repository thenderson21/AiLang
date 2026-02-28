#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_DIR="${ROOT_DIR}/.tmp/aivm-c-build-shared"

cmake -S "${ROOT_DIR}/AiVM.C" -B "${BUILD_DIR}" -DAIVM_BUILD_SHARED=ON
cmake --build "${BUILD_DIR}"

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
