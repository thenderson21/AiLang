#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP_DIR="${ROOT_DIR}/.tmp/aivm-wasm-golden"
CASE_PATH="${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos"
CASE_NAME="vm_c_execute_src_main_params"
PUBLISH_DIR="${TMP_DIR}/publish"
NATIVE_OUT="${TMP_DIR}/native.out"
WASM_OUT="${TMP_DIR}/wasm.out"

cd "${ROOT_DIR}"

if ! command -v wasmtime >/dev/null 2>&1; then
  echo "wasmtime is required to run wasm golden tests" >&2
  exit 1
fi
if ! command -v emcc >/dev/null 2>&1; then
  echo "emcc is required to build wasm runtime artifact for golden tests" >&2
  exit 1
fi

./scripts/build-aivm-wasm.sh >/dev/null

rm -rf "${TMP_DIR}"
mkdir -p "${PUBLISH_DIR}"

./tools/airun publish "${CASE_PATH}" --target wasm32 --out "${PUBLISH_DIR}" >/dev/null

set +e
./tools/airun run "${CASE_PATH}" --vm=c >"${NATIVE_OUT}" 2>&1
native_rc=$?
wasmtime run -C cache=n "${PUBLISH_DIR}/${CASE_NAME}.wasm" - < "${PUBLISH_DIR}/app.aibc1" >"${WASM_OUT}" 2>&1
wasm_rc=$?
set -e

if [[ ${native_rc} -ne ${wasm_rc} ]]; then
  echo "wasm golden mismatch: status native=${native_rc} wasm=${wasm_rc}" >&2
  exit 1
fi

if ! diff -u "${NATIVE_OUT}" "${WASM_OUT}" >/dev/null; then
  echo "wasm golden mismatch: output differs from native baseline" >&2
  diff -u "${NATIVE_OUT}" "${WASM_OUT}" || true
  exit 1
fi

echo "wasm golden: PASS (${CASE_NAME})"
