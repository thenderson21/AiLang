#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="${AIVM_WASM_OUT_DIR:-${ROOT_DIR}/.artifacts/aivm-wasm32}"
OUT_WASM="${OUT_DIR}/aivm-runtime-wasm32.wasm"
NATIVE_INCLUDE="${ROOT_DIR}/src/AiVM.Core/native/include"
NATIVE_SRC_DIR="${ROOT_DIR}/src/AiVM.Core/native/src"

if ! command -v emcc >/dev/null 2>&1; then
  echo "emcc is required to build wasm runtime artifact" >&2
  exit 1
fi

mkdir -p "${OUT_DIR}"

emcc -O2 -std=c17 -Wall -Wextra -Werror \
  -I "${NATIVE_INCLUDE}" \
  "${NATIVE_SRC_DIR}/aivm_types.c" \
  "${NATIVE_SRC_DIR}/aivm_vm.c" \
  "${NATIVE_SRC_DIR}/aivm_program.c" \
  "${NATIVE_SRC_DIR}/aivm_syscall.c" \
  "${NATIVE_SRC_DIR}/aivm_syscall_contracts.c" \
  "${NATIVE_SRC_DIR}/aivm_runtime.c" \
  "${NATIVE_SRC_DIR}/aivm_c_api.c" \
  -s STANDALONE_WASM=1 \
  -s EXPORTED_FUNCTIONS='["_aivm_c_abi_version"]' \
  --no-entry \
  -o "${OUT_WASM}"

echo "${OUT_WASM}"
