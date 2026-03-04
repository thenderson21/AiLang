#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP_ROOT="${ROOT_DIR}/.tmp/aivm-wasm-golden"
TMP_DIR="${TMP_ROOT}/run-$$"
CASES=(
  "vm_c_execute_program_source_gate"
  "vm_c_execute_src_invalid_abi"
  "vm_c_execute_src_main_params"
  "vm_c_execute_src_missing_lib"
  "vm_c_execute_src_missing_main"
  "vm_c_execute_src_remote_call_echo_int"
)
BYTECODE_ONLY_CASES=(
  "sys_remove_bad_type"
  "sys_substring_bad_arity"
  "sys_utf8_bad_type"
  "vm_c_execute_src_node_constant_unsupported"
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
)
MALFORMED_CASES=(
  "vm_c_execute_src_opcode_unmapped"
  "vm_c_execute_src_parse_error"
)
WASM_STDIN_EOF_CASES=(
  "wasm_console_readline_eof"
  "wasm_console_readallstdin_eof"
)
PROCESS_CASE="${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_process_start_unsupported.aos"
FS_WARN_CASE="${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/wasm_profile_warn_fs_file_read.aos"
NET_WARN_CASE="${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/wasm_profile_warn_net_tcp_connect.aos"
PUBLISH_DIR="${TMP_DIR}/publish"
PUBLISH_SPA_DIR="${TMP_DIR}/publish-spa"
PUBLISH_FULLSTACK_DIR="${TMP_DIR}/publish-fullstack"
PUBLISH_PROCESS_CLI_DIR="${TMP_DIR}/publish-process-cli"
NATIVE_OUT="${TMP_DIR}/native.out"
WASM_OUT="${TMP_DIR}/wasm.out"
PROCESS_OUT="${TMP_DIR}/process.out"
PROCESS_ERR="${TMP_DIR}/process.err"
PROCESS_SPA_WARN="${TMP_DIR}/process-spa.warn"
PROCESS_FULLSTACK_WARN="${TMP_DIR}/process-fullstack.warn"
FS_SPA_WARN="${TMP_DIR}/fs-spa.warn"
FS_FULLSTACK_WARN="${TMP_DIR}/fs-fullstack.warn"
NET_SPA_WARN="${TMP_DIR}/net-spa.warn"
NET_FULLSTACK_WARN="${TMP_DIR}/net-fullstack.warn"
MANIFEST_HOST_TARGET_DIR="${TMP_DIR}/manifest-host-target"
MANIFEST_HOST_TARGET_ERR="${TMP_DIR}/manifest-host-target.err"

case "$(uname -s)" in
  Darwin)
    EXPECTED_HOST_RUNTIME_EXT=""
    ;;
  Linux)
    EXPECTED_HOST_RUNTIME_EXT=""
    ;;
  MINGW*|MSYS*|CYGWIN*|Windows_NT)
    EXPECTED_HOST_RUNTIME_EXT=".exe"
    ;;
  *)
    echo "unsupported host OS for wasm golden host runtime assertion" >&2
    exit 1
    ;;
esac
EXPECTED_FULLSTACK_APP_BIN="vm_c_execute_src_main_params${EXPECTED_HOST_RUNTIME_EXT}"

cd "${ROOT_DIR}"
export AIVM_REMOTE_CAPS="cap.remote"
export AIVM_REMOTE_EXPECTED_TOKEN="wasm-golden-token"
export AIVM_REMOTE_SESSION_TOKEN="wasm-golden-token"
export EM_CACHE="${EM_CACHE:-${TMP_ROOT}/emcc-cache}"
HAS_RG=0
if command -v rg >/dev/null 2>&1; then
  HAS_RG=1
fi

contains_regex() {
  local pattern="$1"
  local path="$2"
  if [[ "${HAS_RG}" == "1" ]]; then
    rg -q -- "${pattern}" "${path}"
  else
    grep -Eq -- "${pattern}" "${path}"
  fi
}

contains_fixed() {
  local text="$1"
  local path="$2"
  if [[ "${HAS_RG}" == "1" ]]; then
    rg -Fq -- "${text}" "${path}"
  else
    grep -Fq -- "${text}" "${path}"
  fi
}

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
mkdir -p "${MANIFEST_HOST_TARGET_DIR}"
cleanup() {
  rm -rf "${TMP_DIR}"
}
trap cleanup EXIT

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

for CASE_NAME in "${BYTECODE_ONLY_CASES[@]}"; do
  CASE_PATH="${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/${CASE_NAME}.aos"
  CASE_OUT="${PUBLISH_DIR}/${CASE_NAME}"
  mkdir -p "${CASE_OUT}"
  ./tools/airun publish "${CASE_PATH}" --target wasm32 --out "${CASE_OUT}" >/dev/null

  set +e
  ./tools/airun run "${CASE_OUT}/app.aibc1" --vm=c >"${NATIVE_OUT}" 2>&1
  native_rc=$?
  wasmtime run \
    --env AIVM_REMOTE_CAPS="${AIVM_REMOTE_CAPS}" \
    --env AIVM_REMOTE_EXPECTED_TOKEN="${AIVM_REMOTE_EXPECTED_TOKEN}" \
    --env AIVM_REMOTE_SESSION_TOKEN="${AIVM_REMOTE_SESSION_TOKEN}" \
    -C cache=n "${CASE_OUT}/${CASE_NAME}.wasm" - < "${CASE_OUT}/app.aibc1" >"${WASM_OUT}" 2>&1
  wasm_rc=$?
  set -e

  if [[ ${native_rc} -ne ${wasm_rc} ]]; then
    echo "wasm bytecode-only mismatch (${CASE_NAME}): status native=${native_rc} wasm=${wasm_rc}" >&2
    exit 1
  fi

  if ! diff -u "${NATIVE_OUT}" "${WASM_OUT}" >/dev/null; then
    echo "wasm bytecode-only mismatch (${CASE_NAME}): output differs from native bytecode baseline" >&2
    diff -u "${NATIVE_OUT}" "${WASM_OUT}" || true
    exit 1
  fi
done

STDIN_EXPECTED="${TMP_DIR}/stdin-eof-expected.out"
printf '\n' > "${STDIN_EXPECTED}"
for CASE_NAME in "${WASM_STDIN_EOF_CASES[@]}"; do
  CASE_PATH="${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/${CASE_NAME}.aos"
  CASE_OUT="${PUBLISH_DIR}/${CASE_NAME}"
  mkdir -p "${CASE_OUT}"
  ./tools/airun publish "${CASE_PATH}" --target wasm32 --out "${CASE_OUT}" >/dev/null

  set +e
  wasmtime run \
    --env AIVM_REMOTE_CAPS="${AIVM_REMOTE_CAPS}" \
    --env AIVM_REMOTE_EXPECTED_TOKEN="${AIVM_REMOTE_EXPECTED_TOKEN}" \
    --env AIVM_REMOTE_SESSION_TOKEN="${AIVM_REMOTE_SESSION_TOKEN}" \
    -C cache=n "${CASE_OUT}/${CASE_NAME}.wasm" - < "${CASE_OUT}/app.aibc1" >"${WASM_OUT}" 2>&1
  wasm_rc=$?
  set -e

  if [[ ${wasm_rc} -ne 0 ]]; then
    echo "wasm stdin EOF mismatch (${CASE_NAME}): expected exit 0, got ${wasm_rc}" >&2
    exit 1
  fi
  if ! cmp -s "${STDIN_EXPECTED}" "${WASM_OUT}"; then
    echo "wasm stdin EOF mismatch (${CASE_NAME}): expected deterministic empty-line output" >&2
    diff -u "${STDIN_EXPECTED}" "${WASM_OUT}" || true
    exit 1
  fi
done

for CASE_NAME in "${MALFORMED_CASES[@]}"; do
  CASE_PATH="${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/${CASE_NAME}.aos"
  CASE_OUT="${PUBLISH_DIR}/${CASE_NAME}"
  CASE_ERR="${CASE_OUT}/publish.err"
  mkdir -p "${CASE_OUT}"
  if ./tools/airun publish "${CASE_PATH}" --target wasm32 --out "${CASE_OUT}" >/dev/null 2>"${CASE_ERR}"; then
    echo "wasm publish contract mismatch (${CASE_NAME}): expected deterministic malformed-input publish failure" >&2
    exit 1
  fi
  if ! contains_regex 'Err#err1\(code=DEV008 message="' "${CASE_ERR}"; then
    echo "wasm publish contract mismatch (${CASE_NAME}): expected DEV008 deterministic malformed-input error" >&2
    exit 1
  fi
  case "${CASE_NAME}" in
    vm_c_execute_src_opcode_unmapped)
      if ! contains_regex 'cannot encode this bytecode AOS shape yet' "${CASE_ERR}"; then
        echo "wasm publish contract mismatch (${CASE_NAME}): expected unsupported-opcode-shape reason" >&2
        exit 1
      fi
      ;;
    vm_c_execute_src_parse_error)
      if ! contains_regex 'Publish needs prebuilt \.aibc1 unless source is bytecode-style AOS' "${CASE_ERR}"; then
        echo "wasm publish contract mismatch (${CASE_NAME}): expected non-bytecode-source gate reason" >&2
        exit 1
      fi
      ;;
  esac
done

./tools/airun publish "${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos" --target wasm32 --wasm-profile spa --out "${PUBLISH_SPA_DIR}" >/dev/null
./tools/airun publish "${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos" --target wasm32 --wasm-profile fullstack --out "${PUBLISH_FULLSTACK_DIR}" >/dev/null
./tools/airun publish "${PROCESS_CASE}" --target wasm32 --wasm-profile cli --out "${PUBLISH_PROCESS_CLI_DIR}" >"${PROCESS_OUT}" 2>"${PROCESS_ERR}"
./tools/airun publish "${PROCESS_CASE}" --target wasm32 --wasm-profile spa --out "${TMP_DIR}/process-spa" >/dev/null 2>"${PROCESS_SPA_WARN}"
./tools/airun publish "${PROCESS_CASE}" --target wasm32 --wasm-profile fullstack --out "${TMP_DIR}/process-fullstack" >/dev/null 2>"${PROCESS_FULLSTACK_WARN}"
./tools/airun publish "${FS_WARN_CASE}" --target wasm32 --wasm-profile spa --out "${TMP_DIR}/fs-spa" >/dev/null 2>"${FS_SPA_WARN}"
./tools/airun publish "${FS_WARN_CASE}" --target wasm32 --wasm-profile fullstack --out "${TMP_DIR}/fs-fullstack" >/dev/null 2>"${FS_FULLSTACK_WARN}"
./tools/airun publish "${NET_WARN_CASE}" --target wasm32 --wasm-profile spa --out "${TMP_DIR}/net-spa" >/dev/null 2>"${NET_SPA_WARN}"
./tools/airun publish "${NET_WARN_CASE}" --target wasm32 --wasm-profile fullstack --out "${TMP_DIR}/net-fullstack" >/dev/null 2>"${NET_FULLSTACK_WARN}"
echo "wasm golden corpus: PASS (${#CASES[@]} cases)"
echo "wasm bytecode-only corpus: PASS (${#BYTECODE_ONLY_CASES[@]} cases)"
echo "wasm stdin EOF corpus: PASS (${#WASM_STDIN_EOF_CASES[@]} cases)"
echo "wasm malformed corpus: PASS (${#MALFORMED_CASES[@]} cases)"

if [[ ! -f "${PUBLISH_SPA_DIR}/index.html" || ! -f "${PUBLISH_SPA_DIR}/main.js" || ! -f "${PUBLISH_SPA_DIR}/aivm-runtime-wasm32-web.wasm" ]]; then
  echo "wasm profile mismatch: spa publish did not emit web bootstrap files" >&2
  exit 1
fi
if ! contains_fixed 'AIVM_REMOTE_MODE' "${PUBLISH_SPA_DIR}/main.js"; then
  echo "wasm profile mismatch: spa publish did not emit remote mode switch in main.js" >&2
  exit 1
fi
if ! contains_fixed "AIVM_REMOTE_WS_ENDPOINT" "${PUBLISH_SPA_DIR}/main.js"; then
  echo "wasm profile mismatch: spa publish did not emit websocket endpoint hook in main.js" >&2
  exit 1
fi
if ! contains_fixed "AIVM_REMOTE_MODE=js requires AiLang.remote.call adapter" "${PUBLISH_SPA_DIR}/main.js"; then
  echo "wasm profile mismatch: spa publish did not emit deterministic js-mode adapter diagnostic" >&2
  exit 1
fi
if ! contains_fixed "RUN101: unsupported AIVM_REMOTE_MODE" "${PUBLISH_SPA_DIR}/main.js"; then
  echo "wasm profile mismatch: spa publish did not emit deterministic invalid remote-mode diagnostic" >&2
  exit 1
fi
if ! contains_fixed 'globalThis.AiLang' "${PUBLISH_SPA_DIR}/main.js"; then
  echo "wasm profile mismatch: spa publish did not emit AiLang root bridge in main.js" >&2
  exit 1
fi
if ! contains_fixed 'stdin = {' "${PUBLISH_SPA_DIR}/main.js"; then
  echo "wasm profile mismatch: spa publish did not emit stdin queue API in main.js" >&2
  exit 1
fi
if ! contains_fixed '__aivmStdinRead' "${PUBLISH_SPA_DIR}/main.js"; then
  echo "wasm profile mismatch: spa publish did not emit stdin drain bridge in main.js" >&2
  exit 1
fi
if ! contains_fixed 'console.log' "${PUBLISH_SPA_DIR}/main.js" || ! contains_fixed 'console.error' "${PUBLISH_SPA_DIR}/main.js"; then
  echo "wasm profile mismatch: spa publish did not emit stdout/stderr console mirrors in main.js" >&2
  exit 1
fi
if ! cmp -s "${PUBLISH_SPA_DIR}/aivm-runtime-wasm32-web.wasm" "${ROOT_DIR}/.artifacts/aivm-wasm32/aivm-runtime-wasm32-web.wasm"; then
  echo "wasm profile mismatch: spa publish did not copy web runtime wasm artifact" >&2
  exit 1
fi

if [[ ! -f "${PUBLISH_FULLSTACK_DIR}/README.md" || ! -f "${PUBLISH_FULLSTACK_DIR}/www/index.html" || ! -f "${PUBLISH_FULLSTACK_DIR}/www/main.js" || ! -f "${PUBLISH_FULLSTACK_DIR}/www/app.aibc1" || ! -f "${PUBLISH_FULLSTACK_DIR}/www/aivm-runtime-wasm32-web.wasm" || ! -f "${PUBLISH_FULLSTACK_DIR}/www/aivm-runtime-wasm32-web.mjs" ]]; then
  echo "wasm profile mismatch: fullstack publish did not emit root app + www layout" >&2
  exit 1
fi
if ! contains_fixed 'AIVM_REMOTE_MODE' "${PUBLISH_FULLSTACK_DIR}/www/main.js"; then
  echo "wasm profile mismatch: fullstack publish did not emit remote mode switch in www/main.js" >&2
  exit 1
fi
if ! contains_fixed "AIVM_REMOTE_WS_ENDPOINT" "${PUBLISH_FULLSTACK_DIR}/www/main.js"; then
  echo "wasm profile mismatch: fullstack publish did not emit websocket endpoint hook in www/main.js" >&2
  exit 1
fi
if ! contains_fixed "AIVM_REMOTE_MODE=js requires AiLang.remote.call adapter" "${PUBLISH_FULLSTACK_DIR}/www/main.js"; then
  echo "wasm profile mismatch: fullstack publish did not emit deterministic js-mode adapter diagnostic" >&2
  exit 1
fi
if ! contains_fixed "RUN101: unsupported AIVM_REMOTE_MODE" "${PUBLISH_FULLSTACK_DIR}/www/main.js"; then
  echo "wasm profile mismatch: fullstack publish did not emit deterministic invalid remote-mode diagnostic" >&2
  exit 1
fi
if ! contains_fixed '__aivmRemoteCall' "${PUBLISH_FULLSTACK_DIR}/www/main.js"; then
  echo "wasm profile mismatch: fullstack publish did not emit remote call bridge in www/main.js" >&2
  exit 1
fi
if ! contains_fixed '__aivmStdinRead' "${PUBLISH_FULLSTACK_DIR}/www/main.js"; then
  echo "wasm profile mismatch: fullstack publish did not emit stdin drain bridge in www/main.js" >&2
  exit 1
fi
if ! cmp -s "${PUBLISH_FULLSTACK_DIR}/www/aivm-runtime-wasm32-web.wasm" "${ROOT_DIR}/.artifacts/aivm-wasm32/aivm-runtime-wasm32-web.wasm"; then
  echo "wasm profile mismatch: fullstack www did not copy web runtime wasm artifact" >&2
  exit 1
fi
if [[ ! -f "${PUBLISH_FULLSTACK_DIR}/${EXPECTED_FULLSTACK_APP_BIN}" ]]; then
  echo "wasm profile mismatch: fullstack publish did not emit root app binary ${EXPECTED_FULLSTACK_APP_BIN}" >&2
  exit 1
fi

if [[ -f "${PUBLISH_FULLSTACK_DIR}/server/run-remote-ws-bridge.sh" || -f "${PUBLISH_FULLSTACK_DIR}/server/run-remote-ws-bridge.ps1" || -d "${PUBLISH_FULLSTACK_DIR}/client" || -d "${PUBLISH_FULLSTACK_DIR}/server" ]]; then
  echo "wasm profile mismatch: fullstack publish should not emit C bridge run scripts" >&2
  exit 1
fi
if [[ -f "${PUBLISH_FULLSTACK_DIR}/run" || -f "${PUBLISH_FULLSTACK_DIR}/run.ps1" ]]; then
  echo "wasm profile mismatch: fullstack publish must not emit legacy root run launchers" >&2
  exit 1
fi

mkdir -p "${MANIFEST_HOST_TARGET_DIR}/src"
cp "${PUBLISH_FULLSTACK_DIR}/app.aibc1" "${MANIFEST_HOST_TARGET_DIR}/src/app.aibc1"
cat > "${MANIFEST_HOST_TARGET_DIR}/src/app.aos" <<'EOF'
Program#p1 {
  Let#l1(name=dummy) { Lit#v1(value=1) }
}
EOF
cat > "${MANIFEST_HOST_TARGET_DIR}/project.aiproj" <<'EOF'
Program#p1 {
  Project#proj1(name="manifest_host_target" entryFile="src/app.aos" publishWasmFullstackHostTarget="invalid-rid")
}
EOF
if ./tools/airun publish "${MANIFEST_HOST_TARGET_DIR}/project.aiproj" --target wasm32 --wasm-profile fullstack --out "${MANIFEST_HOST_TARGET_DIR}/out" > /dev/null 2>"${MANIFEST_HOST_TARGET_ERR}"; then
  echo "wasm manifest host-target mismatch: expected publish failure for invalid publishWasmFullstackHostTarget" >&2
  exit 1
fi
if ! contains_regex 'Unsupported wasm fullstack host target RID' "${MANIFEST_HOST_TARGET_ERR}"; then
  echo "wasm manifest host-target mismatch: expected deterministic invalid host target error" >&2
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
if ! contains_regex 'Err#err1\(code=RUN001 message="' "${PROCESS_OUT}"; then
  echo "wasm cli unsupported-capability mismatch: expected RUN001 wrapper code for failed syscall execution" >&2
  exit 1
fi
if ! contains_fixed "Warn#warn1(code=WASM001 message=\"sys.process.spawn is not available on wasm profile 'spa'" "${PROCESS_SPA_WARN}"; then
  echo "wasm spa warning mismatch: expected WASM001 warning for sys.process.spawn" >&2
  exit 1
fi
if ! contains_fixed "Warn#warn1(code=WASM001 message=\"sys.process.spawn is not available on wasm profile 'fullstack'" "${PROCESS_FULLSTACK_WARN}"; then
  echo "wasm fullstack warning mismatch: expected WASM001 warning for sys.process.spawn" >&2
  exit 1
fi
if ! contains_fixed "Warn#warn1(code=WASM001 message=\"sys.fs.file.read is not available on wasm profile 'spa'" "${FS_SPA_WARN}"; then
  echo "wasm spa warning mismatch: expected WASM001 warning for sys.fs.file.read" >&2
  exit 1
fi
if ! contains_fixed "Warn#warn1(code=WASM001 message=\"sys.fs.file.read is not available on wasm profile 'fullstack'" "${FS_FULLSTACK_WARN}"; then
  echo "wasm fullstack warning mismatch: expected WASM001 warning for sys.fs.file.read" >&2
  exit 1
fi
if ! contains_fixed "Warn#warn1(code=WASM001 message=\"sys.net.tcp.connect is not available on wasm profile 'spa'" "${NET_SPA_WARN}"; then
  echo "wasm spa warning mismatch: expected WASM001 warning for sys.net.tcp.connect" >&2
  exit 1
fi
if ! contains_fixed "Warn#warn1(code=WASM001 message=\"sys.net.tcp.connect is not available on wasm profile 'fullstack'" "${NET_FULLSTACK_WARN}"; then
  echo "wasm fullstack warning mismatch: expected WASM001 warning for sys.net.tcp.connect" >&2
  exit 1
fi

echo "wasm golden profiles: PASS (cli/spa/fullstack)"
