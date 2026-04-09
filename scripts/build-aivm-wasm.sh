#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="${AIVM_WASM_OUT_DIR:-${ROOT_DIR}/.artifacts/aivm-wasm32}"
OUT_WASM="${OUT_DIR}/aivm-runtime-wasm32.wasm"
OUT_WEB_JS="${OUT_DIR}/aivm-runtime-wasm32-web.mjs"
NATIVE_INCLUDE="${ROOT_DIR}/src/AiVM.Core/native/include"
NATIVE_SRC_DIR="${ROOT_DIR}/src/AiVM.Core/native"
NATIVE_EXAMPLES_DIR="${ROOT_DIR}/src/AiVM.Core/native/examples"

if ! command -v emcc >/dev/null 2>&1; then
  echo "emcc is required to build wasm runtime artifact" >&2
  exit 1
fi

mkdir -p "${OUT_DIR}"

emcc -O2 -std=c17 -Wall -Wextra -Werror \
  -I "${NATIVE_INCLUDE}" \
  "${NATIVE_EXAMPLES_DIR}/wasm_runner.c" \
  "${NATIVE_SRC_DIR}/aivm_types.c" \
  "${NATIVE_SRC_DIR}/aivm_vm.c" \
  "${NATIVE_SRC_DIR}/aivm_program.c" \
  "${NATIVE_SRC_DIR}/sys/aivm_syscall.c" \
  "${NATIVE_SRC_DIR}/sys/aivm_syscall_contracts.c" \
  "${NATIVE_SRC_DIR}/aivm_runtime.c" \
  "${NATIVE_SRC_DIR}/aivm_c_api.c" \
  "${NATIVE_SRC_DIR}/remote/aivm_remote_channel.c" \
  "${NATIVE_SRC_DIR}/remote/aivm_remote_session.c" \
  -s STANDALONE_WASM=1 \
  -s FILESYSTEM=1 \
  -s STACK_SIZE=262144 \
  -o "${OUT_WASM}"

emcc -O2 -std=c17 -Wall -Wextra -Werror \
  -DAIVM_WASM_WEB=1 \
  -I "${NATIVE_INCLUDE}" \
  "${NATIVE_EXAMPLES_DIR}/wasm_runner.c" \
  "${NATIVE_SRC_DIR}/aivm_types.c" \
  "${NATIVE_SRC_DIR}/aivm_vm.c" \
  "${NATIVE_SRC_DIR}/aivm_program.c" \
  "${NATIVE_SRC_DIR}/sys/aivm_syscall.c" \
  "${NATIVE_SRC_DIR}/sys/aivm_syscall_contracts.c" \
  "${NATIVE_SRC_DIR}/aivm_runtime.c" \
  "${NATIVE_SRC_DIR}/aivm_c_api.c" \
  "${NATIVE_SRC_DIR}/remote/aivm_remote_channel.c" \
  "${NATIVE_SRC_DIR}/remote/aivm_remote_session.c" \
  -s MODULARIZE=1 \
  -s EXPORT_ES6=1 \
  -s ENVIRONMENT=web \
  -s INVOKE_RUN=0 \
  -s EXPORTED_RUNTIME_METHODS=FS,callMain \
  -s ASYNCIFY \
  -s STACK_SIZE=262144 \
  -o "${OUT_WEB_JS}"

echo "${OUT_WASM}"
echo "${OUT_WEB_JS}"
