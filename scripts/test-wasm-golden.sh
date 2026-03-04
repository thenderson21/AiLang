#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP_DIR="${ROOT_DIR}/.tmp/aivm-wasm-golden"
CASES=(
  "vm_c_execute_program_source_gate"
  "vm_c_execute_src_invalid_abi"
  "vm_c_execute_src_main_params"
  "vm_c_execute_src_missing_lib"
  "vm_c_execute_src_missing_main"
  "vm_c_execute_src_remote_call_echo_int"
)
UNSUPPORTED_CASES=(
  "sys_remove_bad_type"
  "sys_substring_bad_arity"
  "sys_utf8_bad_type"
  "vm_c_execute_src_async_call_negative"
  "vm_c_execute_src_async_call_oob"
  "vm_c_execute_src_async_callsys_bad_slot"
  "vm_c_execute_src_await_unsupported"
  "vm_c_execute_src_call_negative"
  "vm_c_execute_src_call_oob"
  "vm_c_execute_src_callsys_bad_slot"
  "vm_c_execute_src_invalid_abi_whitespace"
  "vm_c_execute_src_invalid_abi_whitespace_only"
  "vm_c_execute_src_jump_if_false_negative"
  "vm_c_execute_src_jump_if_false_oob"
  "vm_c_execute_src_jump_negative"
  "vm_c_execute_src_jump_oob"
  "vm_c_execute_src_make_node_unsupported"
  "vm_c_execute_src_nonmain_params"
  "vm_c_execute_src_par_begin_unsupported"
  "vm_c_execute_src_par_cancel_unsupported"
  "vm_c_execute_src_par_fork_unsupported"
  "vm_c_execute_src_par_join_unsupported"
  "vm_c_execute_src_node_constant_unsupported"
  "vm_c_execute_src_opcode_unmapped"
  "vm_c_execute_src_parse_error"
)
PROCESS_CASE="${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_process_start_unsupported.aos"
PUBLISH_DIR="${TMP_DIR}/publish"
PUBLISH_SPA_DIR="${TMP_DIR}/publish-spa"
PUBLISH_FULLSTACK_DIR="${TMP_DIR}/publish-fullstack"
PUBLISH_PROCESS_CLI_DIR="${TMP_DIR}/publish-process-cli"
NATIVE_OUT="${TMP_DIR}/native.out"
WASM_OUT="${TMP_DIR}/wasm.out"
PROCESS_OUT="${TMP_DIR}/process.out"
PROCESS_ERR="${TMP_DIR}/process.err"

cd "${ROOT_DIR}"
export AIVM_REMOTE_CAPS="cap.remote"
export AIVM_REMOTE_EXPECTED_TOKEN="wasm-golden-token"
export AIVM_REMOTE_SESSION_TOKEN="wasm-golden-token"

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
mkdir -p "${PUBLISH_SPA_DIR}"
mkdir -p "${PUBLISH_FULLSTACK_DIR}"
mkdir -p "${PUBLISH_PROCESS_CLI_DIR}"

for CASE_NAME in "${CASES[@]}"; do
  CASE_PATH="${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/${CASE_NAME}.aos"
  CASE_OUT="${PUBLISH_DIR}/${CASE_NAME}"
  mkdir -p "${CASE_OUT}"
  ./tools/airun publish "${CASE_PATH}" --target wasm32 --out "${CASE_OUT}" >/dev/null

  set +e
  ./tools/airun run "${CASE_PATH}" --vm=c >"${NATIVE_OUT}" 2>&1
  native_rc=$?
  wasmtime run \
    --env AIVM_REMOTE_CAPS="${AIVM_REMOTE_CAPS}" \
    --env AIVM_REMOTE_EXPECTED_TOKEN="${AIVM_REMOTE_EXPECTED_TOKEN}" \
    --env AIVM_REMOTE_SESSION_TOKEN="${AIVM_REMOTE_SESSION_TOKEN}" \
    -C cache=n "${CASE_OUT}/${CASE_NAME}.wasm" - < "${CASE_OUT}/app.aibc1" >"${WASM_OUT}" 2>&1
  wasm_rc=$?
  set -e

  if [[ ${native_rc} -ne ${wasm_rc} ]]; then
    echo "wasm golden mismatch (${CASE_NAME}): status native=${native_rc} wasm=${wasm_rc}" >&2
    exit 1
  fi

  if ! diff -u "${NATIVE_OUT}" "${WASM_OUT}" >/dev/null; then
    echo "wasm golden mismatch (${CASE_NAME}): output differs from native baseline" >&2
    diff -u "${NATIVE_OUT}" "${WASM_OUT}" || true
    exit 1
  fi
done

for CASE_NAME in "${UNSUPPORTED_CASES[@]}"; do
  CASE_PATH="${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/${CASE_NAME}.aos"
  CASE_OUT="${PUBLISH_DIR}/${CASE_NAME}"
  mkdir -p "${CASE_OUT}"
  if ./tools/airun publish "${CASE_PATH}" --target wasm32 --out "${CASE_OUT}" >/dev/null 2>&1; then
    echo "wasm publish contract mismatch (${CASE_NAME}): expected deterministic unsupported publish failure" >&2
    exit 1
  fi
done

./tools/airun publish "${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos" --target wasm32 --wasm-profile spa --out "${PUBLISH_SPA_DIR}" >/dev/null
./tools/airun publish "${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos" --target wasm32 --wasm-profile fullstack --out "${PUBLISH_FULLSTACK_DIR}" >/dev/null
./tools/airun publish "${PROCESS_CASE}" --target wasm32 --wasm-profile cli --out "${PUBLISH_PROCESS_CLI_DIR}" >"${PROCESS_OUT}" 2>"${PROCESS_ERR}"
echo "wasm golden corpus: PASS (${#CASES[@]} cases)"
echo "wasm unsupported corpus: PASS (${#UNSUPPORTED_CASES[@]} cases)"

if [[ ! -f "${PUBLISH_SPA_DIR}/index.html" || ! -f "${PUBLISH_SPA_DIR}/main.js" ]]; then
  echo "wasm profile mismatch: spa publish did not emit web bootstrap files" >&2
  exit 1
fi

if [[ ! -f "${PUBLISH_FULLSTACK_DIR}/client/index.html" || ! -f "${PUBLISH_FULLSTACK_DIR}/server/README.md" || ! -f "${PUBLISH_FULLSTACK_DIR}/server/project.aiproj" || ! -f "${PUBLISH_FULLSTACK_DIR}/server/src/app.aos" ]]; then
  echo "wasm profile mismatch: fullstack publish did not emit AiLang server scaffold layout" >&2
  exit 1
fi

if [[ -f "${PUBLISH_FULLSTACK_DIR}/server/run-remote-ws-bridge.sh" || -f "${PUBLISH_FULLSTACK_DIR}/server/run-remote-ws-bridge.ps1" ]]; then
  echo "wasm profile mismatch: fullstack publish should not emit C bridge run scripts" >&2
  exit 1
fi

set +e
wasmtime run \
  --env AIVM_REMOTE_CAPS="${AIVM_REMOTE_CAPS}" \
  --env AIVM_REMOTE_EXPECTED_TOKEN="${AIVM_REMOTE_EXPECTED_TOKEN}" \
  --env AIVM_REMOTE_SESSION_TOKEN="${AIVM_REMOTE_SESSION_TOKEN}" \
  -C cache=n "${PUBLISH_PROCESS_CLI_DIR}/vm_c_execute_src_process_start_unsupported.wasm" - < "${PUBLISH_PROCESS_CLI_DIR}/app.aibc1" >"${PROCESS_OUT}" 2>&1
process_rc=$?
set -e
if [[ ${process_rc} -ne 3 ]]; then
  echo "wasm cli unsupported-capability mismatch: expected exit 3 for sys.process.spawn, got ${process_rc}" >&2
  exit 1
fi
if ! rg -q 'Err#err1\(code=RUN001 message="' "${PROCESS_OUT}"; then
  echo "wasm cli unsupported-capability mismatch: expected RUN001 wrapper code for failed syscall execution" >&2
  exit 1
fi

echo "wasm golden profiles: PASS (cli/spa/fullstack)"
