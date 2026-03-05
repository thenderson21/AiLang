#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP_ROOT="${ROOT_DIR}/.tmp/aivm-wasm-golden"
TMP_DIR="${TMP_ROOT}/run-$$"
CASES=(
  "vm_c_execute_src_invalid_abi"
  "vm_c_execute_src_main_params"
  "vm_c_execute_src_missing_lib"
  "vm_c_execute_src_missing_main"
  "vm_c_execute_src_remote_call_echo_int"
)
BYTECODE_ONLY_CASES=(
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
  "sys_remove_bad_type"
  "sys_substring_bad_arity"
  "sys_utf8_bad_type"
  "vm_c_execute_program_source_gate"
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
UI_WARN_CASE="${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/wasm_profile_warn_ui_draw_rect.aos"
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
UI_SPA_WARN="${TMP_DIR}/ui-spa.warn"
UI_FULLSTACK_WARN="${TMP_DIR}/ui-fullstack.warn"
UI_CLI_WARN="${TMP_DIR}/ui-cli.warn"
MANIFEST_HOST_TARGET_DIR="${TMP_DIR}/manifest-host-target"
MANIFEST_HOST_TARGET_ERR="${TMP_DIR}/manifest-host-target.err"
FULLSTACK_HOST_STDOUT="${TMP_DIR}/fullstack-host.stdout"
FULLSTACK_HOST_STDERR="${TMP_DIR}/fullstack-host.stderr"

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

run_web_runtime_js_mode_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ "${label}" == "fullstack" ]]; then
    app_fetch_path="./app.aibc1"
  fi
  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for runtime execution check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for runtime execution check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: {
      writeFile(path, bytes) {
        globalThis.__aivmWriteFile = { path, size: bytes.length };
      }
    },
    print: null,
    printErr: null,
    callMain(argv) {
      globalThis.__aivmCallMainArgv = argv.slice();
      if (typeof this.print === "function") {
        this.print("main-ok");
      }
      if (typeof this.printErr === "function") {
        this.printErr("main-err");
      }
    }
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm check missing required environment values');
}

const logs = [];
const errs = [];
const remoteCalls = [];

globalThis.location = { hostname: 'localhost' };
const outputNode = { textContent: '' };
globalThis.document = {
  getElementById(id) {
    if (id === 'output') {
      return outputNode;
    }
    return null;
  }
};
globalThis.console = {
  log(value) { logs.push(String(value)); },
  error(value) { errs.push(String(value)); }
};
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) {
    throw new Error(`unexpected fetch path: ${String(url)}`);
  }
  return {
    async arrayBuffer() {
      return new Uint8Array([0x41, 0x69, 0x42, 0x43, 0x31]).buffer;
    }
  };
};
globalThis.AIVM_REMOTE_MODE = 'js';
globalThis.AiLang = {
  remote: {
    async call(cap, op, value) {
      remoteCalls.push({ cap, op, value });
      return 1337;
    }
  }
};

await import(pathToFileURL(mainJsPath).href);

if (!globalThis.AiLang || !globalThis.AiLang.stdin) {
  throw new Error('AiLang stdin bridge missing after web bootstrap');
}
globalThis.AiLang.stdin.push('first');
globalThis.AiLang.stdin.push('second');
if (globalThis.__aivmStdinRead() !== 'first') {
  throw new Error('stdin FIFO mismatch at first value');
}
if (globalThis.__aivmStdinRead() !== 'second') {
  throw new Error('stdin FIFO mismatch at second value');
}
if (globalThis.__aivmStdinRead() !== '') {
  throw new Error('stdin open-empty must yield empty string');
}
const hostReads = ['host-only', undefined];
globalThis.AIVM_HOST_STDIN_READ = () => {
  if (hostReads.length === 0) {
    return undefined;
  }
  return hostReads.shift();
};
globalThis.AiLang.stdin.push('queue-wins');
if (globalThis.__aivmStdinRead() !== 'queue-wins') {
  throw new Error('stdin queue must take precedence over host callback');
}
if (globalThis.__aivmStdinRead() !== 'host-only') {
  throw new Error('stdin host callback fallback mismatch');
}
if (globalThis.__aivmStdinRead() !== '') {
  throw new Error('stdin host callback undefined must preserve open-empty semantics');
}
globalThis.AiLang.stdin.close();
if (globalThis.__aivmStdinRead() !== null) {
  throw new Error('stdin closed-empty must yield null');
}

const remoteResult = await globalThis.__aivmRemoteCall('cap.remote', 'echo', 42);
if (remoteResult !== 1337) {
  throw new Error(`remote call result mismatch: ${String(remoteResult)}`);
}
if (remoteCalls.length !== 1 ||
    remoteCalls[0].cap !== 'cap.remote' ||
    remoteCalls[0].op !== 'echo' ||
    remoteCalls[0].value !== 42) {
  throw new Error('remote call wiring mismatch in js mode');
}

if (!globalThis.__aivmWriteFile ||
    globalThis.__aivmWriteFile.path !== '/app.aibc1' ||
    globalThis.__aivmWriteFile.size <= 0) {
  throw new Error('runtime writeFile wiring mismatch');
}
if (!globalThis.__aivmCallMainArgv ||
    globalThis.__aivmCallMainArgv.length !== 1 ||
    globalThis.__aivmCallMainArgv[0] !== '/app.aibc1') {
  throw new Error('runtime callMain argv mismatch');
}
if (!logs.includes('main-ok')) {
  throw new Error('runtime print mirror mismatch');
}
if (!errs.includes('main-err')) {
  throw new Error('runtime printErr mirror mismatch');
}
if (!outputNode.textContent.includes('main-ok') ||
    !outputNode.textContent.includes('main-err')) {
  throw new Error('browser output textContent mirror mismatch');
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_js_mode_missing_adapter_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-missing-adapter.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for missing-adapter check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for missing-adapter check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm missing-adapter check missing required environment values');
}

globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async () => ({ async arrayBuffer() { return new Uint8Array([1]).buffer; } });
globalThis.AIVM_REMOTE_MODE = 'js';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);

let message = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 7);
  throw new Error('expected remote call to reject in js mode without adapter');
} catch (err) {
  message = String(err && err.message ? err.message : err);
}
if (!message.includes('AIVM_REMOTE_MODE=js requires AiLang.remote.call adapter')) {
  throw new Error(`unexpected missing-adapter message: ${message}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_invalid_mode_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-invalid-mode.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for invalid-mode check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for invalid-mode check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
if (!mainJsPath) {
  throw new Error('node wasm invalid-mode check missing main path');
}

globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async () => ({ async arrayBuffer() { return new Uint8Array([1]).buffer; } });
globalThis.AIVM_REMOTE_MODE = 'invalid-mode';
globalThis.AiLang = { remote: {} };

let message = '';
try {
  await import(pathToFileURL(mainJsPath).href);
  throw new Error('expected invalid mode import to throw');
} catch (err) {
  message = String(err && err.message ? err.message : err);
}
if (!message.includes("RUN101: unsupported AIVM_REMOTE_MODE 'invalid-mode'")) {
  throw new Error(`unexpected invalid mode message: ${message}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_mode_call_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-mode.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws-mode check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws-mode check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws-mode check missing required environment values');
}

function writeU16LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
}
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function readU32LE(arr, off) {
  return (arr[off] | (arr[off + 1] << 8) | (arr[off + 2] << 16) | (arr[off + 3] << 24)) >>> 0;
}
function encodeStr(s) {
  const b = new TextEncoder().encode(s);
  const out = new Uint8Array(2 + b.length);
  writeU16LE(out, 0, b.length);
  out.set(b, 2);
  return out;
}
function decodeStr(arr, off) {
  const n = arr[off] | (arr[off + 1] << 8);
  const start = off + 2;
  const txt = new TextDecoder().decode(arr.slice(start, start + n));
  return { value: txt, next: start + n };
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}

let openedUrl = '';
let callCount = 0;

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor(url) {
    openedUrl = String(url);
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') {
        this.onopen();
      }
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    const id = readU32LE(bytes, 1);
    const payloadLen = readU32LE(bytes, 5);
    const payload = bytes.slice(9, 9 + payloadLen);
    if (type === 0x01) {
      const payloadOutParts = [];
      const caps = ['cap.remote'];
      const header = new Uint8Array(6);
      writeU16LE(header, 0, 1);
      writeU32LE(header, 2, caps.length);
      payloadOutParts.push(header);
      for (const c of caps) {
        payloadOutParts.push(encodeStr(c));
      }
      const total = payloadOutParts.reduce((a, b) => a + b.length, 0);
      const payloadOut = new Uint8Array(total);
      let off = 0;
      for (const part of payloadOutParts) {
        payloadOut.set(part, off);
        off += part.length;
      }
      if (typeof this.onmessage === 'function') {
        this.onmessage({ data: frame(0x02, id, payloadOut) });
      }
      return;
    }
    if (type === 0x10) {
      callCount += 1;
      const capDecoded = decodeStr(payload, 0);
      const opDecoded = decodeStr(payload, capDecoded.next);
      if (capDecoded.value !== 'cap.remote' || opDecoded.value !== 'echo') {
        throw new Error(`unexpected ws call target ${capDecoded.value}/${opDecoded.value}`);
      }
      const payloadOut = new Uint8Array(8);
      const dv = new DataView(payloadOut.buffer);
      dv.setBigInt64(0, 4242n, true);
      if (typeof this.onmessage === 'function') {
        this.onmessage({ data: frame(0x11, id, payloadOut) });
      }
      return;
    }
    throw new Error(`unexpected ws frame type ${type}`);
  }
  close() {
    this.readyState = FakeWebSocket.CLOSED;
    if (typeof this.onclose === 'function') {
      this.onclose();
    }
  }
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) {
    throw new Error(`unexpected fetch path: ${String(url)}`);
  }
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AIVM_REMOTE_WS_ENDPOINT = 'ws://127.0.0.1:8765';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);
const value = await globalThis.__aivmRemoteCall('cap.remote', 'echo', 21);
if (value !== 4242) {
  throw new Error(`unexpected ws call result ${String(value)}`);
}
if (callCount !== 1) {
  throw new Error(`unexpected ws call count ${callCount}`);
}
if (openedUrl !== 'ws://127.0.0.1:8765') {
  throw new Error(`unexpected ws endpoint ${openedUrl}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_mode_deny_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-deny.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws-deny check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws-deny check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws-deny check missing required environment values');
}

function writeU16LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
}
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}
function encodeErrorPayload(code, msg) {
  const msgB = new TextEncoder().encode(msg);
  const out = new Uint8Array(4 + 2 + msgB.length);
  writeU32LE(out, 0, code >>> 0);
  writeU16LE(out, 4, msgB.length);
  out.set(msgB, 6);
  return out;
}

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor(url) {
    this.url = String(url);
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') {
        this.onopen();
      }
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    if (type === 0x01 && typeof this.onmessage === 'function') {
      const deny = encodeErrorPayload(7, 'missing capability');
      this.onmessage({ data: frame(0x03, 1, deny) });
      return;
    }
    throw new Error(`unexpected frame after deny: ${type}`);
  }
  close() {
    this.readyState = FakeWebSocket.CLOSED;
    if (typeof this.onclose === 'function') {
      this.onclose();
    }
  }
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) {
    throw new Error(`unexpected fetch path: ${String(url)}`);
  }
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AIVM_REMOTE_WS_ENDPOINT = 'ws://127.0.0.1:8765';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);
let message = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 99);
  throw new Error('expected ws call to fail on handshake deny');
} catch (err) {
  message = String(err && err.message ? err.message : err);
}
if (!message.includes('remote handshake denied 7: missing capability')) {
  throw new Error(`unexpected ws deny message: ${message}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_mode_call_error_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-call-error.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws-call-error check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws-call-error check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws-call-error check missing required environment values');
}

function writeU16LE(arr, off, v) { arr[off] = v & 255; arr[off + 1] = (v >> 8) & 255; }
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function readU32LE(arr, off) {
  return (arr[off] | (arr[off + 1] << 8) | (arr[off + 2] << 16) | (arr[off + 3] << 24)) >>> 0;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}
function encodeErrorPayload(code, msg) {
  const b = new TextEncoder().encode(msg);
  const out = new Uint8Array(4 + 2 + b.length);
  writeU32LE(out, 0, code >>> 0);
  writeU16LE(out, 4, b.length);
  out.set(b, 6);
  return out;
}
function encodeWelcome() {
  const out = new Uint8Array(6);
  writeU16LE(out, 0, 1);
  writeU32LE(out, 2, 0);
  return out;
}

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') {
        this.onopen();
      }
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    const id = readU32LE(bytes, 1);
    if (type === 0x01) {
      if (typeof this.onmessage === 'function') {
        this.onmessage({ data: frame(0x02, 1, encodeWelcome()) });
      }
      return;
    }
    if (type === 0x10) {
      if (typeof this.onmessage === 'function') {
        this.onmessage({ data: frame(0x12, id, encodeErrorPayload(55, 'call denied')) });
      }
      return;
    }
    throw new Error(`unexpected frame type ${type}`);
  }
  close() {
    this.readyState = FakeWebSocket.CLOSED;
    if (typeof this.onclose === 'function') {
      this.onclose();
    }
  }
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) {
    throw new Error(`unexpected fetch path: ${String(url)}`);
  }
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);
let message = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 1);
  throw new Error('expected ws call to fail on ERROR frame');
} catch (err) {
  message = String(err && err.message ? err.message : err);
}
if (!message.includes('remote 55: call denied')) {
  throw new Error(`unexpected ws call ERROR message: ${message}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_mode_socket_error_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-socket-error.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws-socket-error check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws-socket-error check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws-socket-error check missing required environment values');
}

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      if (typeof this.onerror === 'function') {
        this.onerror(new Error('boom'));
      }
    });
  }
  send() {}
  close() {
    this.readyState = FakeWebSocket.CLOSED;
    if (typeof this.onclose === 'function') {
      this.onclose();
    }
  }
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) {
    throw new Error(`unexpected fetch path: ${String(url)}`);
  }
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);
let message = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 1);
  throw new Error('expected ws call to fail on socket error');
} catch (err) {
  message = String(err && err.message ? err.message : err);
}
if (!message.includes('remote websocket error')) {
  throw new Error(`unexpected ws socket error message: ${message}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_default_endpoint_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-default-endpoint.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws-default-endpoint check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws-default-endpoint check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws-default-endpoint check missing required environment values');
}

function writeU16LE(arr, off, v) { arr[off] = v & 255; arr[off + 1] = (v >> 8) & 255; }
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function readU32LE(arr, off) {
  return (arr[off] | (arr[off + 1] << 8) | (arr[off + 2] << 16) | (arr[off + 3] << 24)) >>> 0;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}
function encodeWelcome() {
  const out = new Uint8Array(6);
  writeU16LE(out, 0, 1);
  writeU32LE(out, 2, 0);
  return out;
}

let openedUrl = '';

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor(url) {
    openedUrl = String(url);
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') {
        this.onopen();
      }
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    const id = readU32LE(bytes, 1);
    if (type === 0x01 && typeof this.onmessage === 'function') {
      this.onmessage({ data: frame(0x02, 1, encodeWelcome()) });
      return;
    }
    if (type === 0x10 && typeof this.onmessage === 'function') {
      this.onmessage({ data: frame(0x12, id, encodeErrorPayload(0, 'ok')) });
    }
  }
  close() {
    this.readyState = FakeWebSocket.CLOSED;
    if (typeof this.onclose === 'function') {
      this.onclose();
    }
  }
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'example-host' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) {
    throw new Error(`unexpected fetch path: ${String(url)}`);
  }
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);
await globalThis.__aivmRemoteCall('cap.remote', 'echo', 1).catch(() => {});
if (openedUrl !== 'ws://example-host:8765') {
  throw new Error(`unexpected default ws endpoint ${openedUrl}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_unexpected_call_frame_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-unexpected-call-frame.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws unexpected-call-frame check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws unexpected-call-frame check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws unexpected-call-frame check missing required environment values');
}

function writeU16LE(arr, off, v) { arr[off] = v & 255; arr[off + 1] = (v >> 8) & 255; }
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function readU32LE(arr, off) {
  return (arr[off] | (arr[off + 1] << 8) | (arr[off + 2] << 16) | (arr[off + 3] << 24)) >>> 0;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}
function encodeWelcome() {
  const out = new Uint8Array(6);
  writeU16LE(out, 0, 1);
  writeU32LE(out, 2, 0);
  return out;
}

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') this.onopen();
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    const id = readU32LE(bytes, 1);
    if (type === 0x01) {
      if (typeof this.onmessage === 'function') this.onmessage({ data: frame(0x02, 1, encodeWelcome()) });
      return;
    }
    if (type === 0x10) {
      if (typeof this.onmessage === 'function') this.onmessage({ data: frame(0x30, id, new Uint8Array(0)) });
      return;
    }
  }
  close() {}
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) throw new Error(`unexpected fetch path: ${String(url)}`);
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);
let message = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 3);
  throw new Error('expected ws call to fail on unexpected call frame');
} catch (err) {
  message = String(err && err.message ? err.message : err);
}
if (!message.includes('remote unexpected frame type 48')) {
  throw new Error(`unexpected call frame message: ${message}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_unexpected_handshake_frame_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-unexpected-handshake-frame.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws unexpected-handshake-frame check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws unexpected-handshake-frame check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws unexpected-handshake-frame check missing required environment values');
}

function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') this.onopen();
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    if (bytes[0] === 0x01 && typeof this.onmessage === 'function') {
      this.onmessage({ data: frame(0x41, 1, new Uint8Array(0)) });
    }
  }
  close() {}
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) throw new Error(`unexpected fetch path: ${String(url)}`);
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);
let message = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 3);
  throw new Error('expected ws call to fail on unexpected handshake frame');
} catch (err) {
  message = String(err && err.message ? err.message : err);
}
if (!message.includes('remote unexpected handshake frame type 65')) {
  throw new Error(`unexpected handshake frame message: ${message}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_pending_close_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-pending-close.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws pending-close check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws pending-close check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws pending-close check missing required environment values');
}

function writeU16LE(arr, off, v) { arr[off] = v & 255; arr[off + 1] = (v >> 8) & 255; }
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}
function encodeWelcome() {
  const out = new Uint8Array(6);
  writeU16LE(out, 0, 1);
  writeU32LE(out, 2, 0);
  return out;
}

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') this.onopen();
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    if (type === 0x01 && typeof this.onmessage === 'function') {
      this.onmessage({ data: frame(0x02, 1, encodeWelcome()) });
      return;
    }
    if (type === 0x10 && typeof this.onclose === 'function') {
      this.readyState = FakeWebSocket.CLOSED;
      this.onclose();
      return;
    }
  }
  close() {
    this.readyState = FakeWebSocket.CLOSED;
    if (typeof this.onclose === 'function') this.onclose();
  }
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) throw new Error(`unexpected fetch path: ${String(url)}`);
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);
let message = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 9);
  throw new Error('expected ws call to fail when socket closes with pending call');
} catch (err) {
  message = String(err && err.message ? err.message : err);
}
if (!message.includes('remote websocket closed')) {
  throw new Error(`unexpected pending-close message: ${message}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_pending_error_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-pending-error.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws pending-error check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws pending-error check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws pending-error check missing required environment values');
}

function writeU16LE(arr, off, v) { arr[off] = v & 255; arr[off + 1] = (v >> 8) & 255; }
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}
function encodeWelcome() {
  const out = new Uint8Array(6);
  writeU16LE(out, 0, 1);
  writeU32LE(out, 2, 0);
  return out;
}

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') this.onopen();
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    if (type === 0x01 && typeof this.onmessage === 'function') {
      this.onmessage({ data: frame(0x02, 1, encodeWelcome()) });
      return;
    }
    if (type === 0x10 && typeof this.onerror === 'function') {
      this.onerror(new Error('boom'));
      return;
    }
  }
  close() {}
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) throw new Error(`unexpected fetch path: ${String(url)}`);
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);
let message = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 11);
  throw new Error('expected ws call to fail when socket errors with pending call');
} catch (err) {
  message = String(err && err.message ? err.message : err);
}
if (!message.includes('remote websocket error')) {
  throw new Error(`unexpected pending-error message: ${message}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_unknown_id_ignored_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-unknown-id-ignored.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws unknown-id check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws unknown-id check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws unknown-id check missing required environment values');
}

function writeU16LE(arr, off, v) { arr[off] = v & 255; arr[off + 1] = (v >> 8) & 255; }
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function readU32LE(arr, off) {
  return (arr[off] | (arr[off + 1] << 8) | (arr[off + 2] << 16) | (arr[off + 3] << 24)) >>> 0;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}
function encodeWelcome() {
  const out = new Uint8Array(6);
  writeU16LE(out, 0, 1);
  writeU32LE(out, 2, 0);
  return out;
}
function encodeResult(value) {
  const out = new Uint8Array(8);
  const dv = new DataView(out.buffer);
  dv.setBigInt64(0, BigInt(value), true);
  return out;
}

let sawUnknown = false;
let sawExpected = false;

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') this.onopen();
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    const id = readU32LE(bytes, 1);
    if (type === 0x01 && typeof this.onmessage === 'function') {
      this.onmessage({ data: frame(0x02, 1, encodeWelcome()) });
      return;
    }
    if (type === 0x10 && typeof this.onmessage === 'function') {
      // Unknown response id should be ignored.
      this.onmessage({ data: frame(0x11, id + 1000, encodeResult(7)) });
      sawUnknown = true;
      // Correct id should resolve the pending call.
      this.onmessage({ data: frame(0x11, id, encodeResult(5150)) });
      sawExpected = true;
      return;
    }
  }
  close() {}
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) throw new Error(`unexpected fetch path: ${String(url)}`);
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);
const value = await globalThis.__aivmRemoteCall('cap.remote', 'echo', 13);
if (value !== 5150) {
  throw new Error(`unexpected ws value after unknown-id frame: ${String(value)}`);
}
if (!sawUnknown || !sawExpected) {
  throw new Error('ws unknown-id test did not exercise both unknown and expected frame paths');
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_handshake_close_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-handshake-close.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws handshake-close check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws handshake-close check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws handshake-close check missing required environment values');
}

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.CLOSED;
      if (typeof this.onclose === 'function') this.onclose();
    });
  }
  send() {}
  close() {
    this.readyState = FakeWebSocket.CLOSED;
    if (typeof this.onclose === 'function') this.onclose();
  }
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) throw new Error(`unexpected fetch path: ${String(url)}`);
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);
let message = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 17);
  throw new Error('expected ws call to fail on close before handshake');
} catch (err) {
  message = String(err && err.message ? err.message : err);
}
if (!message.includes('remote websocket closed')) {
  throw new Error(`unexpected handshake-close message: ${message}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_reconnect_after_error_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-reconnect-after-error.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws reconnect-after-error check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws reconnect-after-error check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws reconnect-after-error check missing required environment values');
}

function writeU16LE(arr, off, v) { arr[off] = v & 255; arr[off + 1] = (v >> 8) & 255; }
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function readU32LE(arr, off) {
  return (arr[off] | (arr[off + 1] << 8) | (arr[off + 2] << 16) | (arr[off + 3] << 24)) >>> 0;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}
function encodeWelcome() {
  const out = new Uint8Array(6);
  writeU16LE(out, 0, 1);
  writeU32LE(out, 2, 0);
  return out;
}
function encodeResult(value) {
  const out = new Uint8Array(8);
  const dv = new DataView(out.buffer);
  dv.setBigInt64(0, BigInt(value), true);
  return out;
}

let constructorCount = 0;
let callFramesSeen = 0;

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.instanceId = ++constructorCount;
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') this.onopen();
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    const id = readU32LE(bytes, 1);
    if (type === 0x01 && typeof this.onmessage === 'function') {
      this.onmessage({ data: frame(0x02, 1, encodeWelcome()) });
      return;
    }
    if (type === 0x10) {
      callFramesSeen += 1;
      if (this.instanceId === 1) {
        if (typeof this.onerror === 'function') this.onerror(new Error('first-socket-error'));
      } else if (typeof this.onmessage === 'function') {
        this.onmessage({ data: frame(0x11, id, encodeResult(9090)) });
      }
      return;
    }
  }
  close() {
    this.readyState = FakeWebSocket.CLOSED;
    if (typeof this.onclose === 'function') this.onclose();
  }
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) throw new Error(`unexpected fetch path: ${String(url)}`);
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);

let firstError = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 21);
  throw new Error('expected first ws call to fail');
} catch (err) {
  firstError = String(err && err.message ? err.message : err);
}
if (!firstError.includes('remote websocket error')) {
  throw new Error(`unexpected first error: ${firstError}`);
}

const secondValue = await globalThis.__aivmRemoteCall('cap.remote', 'echo', 22);
if (secondValue !== 9090) {
  throw new Error(`unexpected second ws value: ${String(secondValue)}`);
}
if (constructorCount < 2) {
  throw new Error(`expected reconnect to create a second websocket, saw ${constructorCount}`);
}
if (callFramesSeen < 2) {
  throw new Error(`expected two call frames (before/after reconnect), saw ${callFramesSeen}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_reconnect_after_deny_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-reconnect-after-deny.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws reconnect-after-deny check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws reconnect-after-deny check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws reconnect-after-deny check missing required environment values');
}

function writeU16LE(arr, off, v) { arr[off] = v & 255; arr[off + 1] = (v >> 8) & 255; }
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function readU32LE(arr, off) {
  return (arr[off] | (arr[off + 1] << 8) | (arr[off + 2] << 16) | (arr[off + 3] << 24)) >>> 0;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}
function encodeWelcome() {
  const out = new Uint8Array(6);
  writeU16LE(out, 0, 1);
  writeU32LE(out, 2, 0);
  return out;
}
function encodeResult(value) {
  const out = new Uint8Array(8);
  const dv = new DataView(out.buffer);
  dv.setBigInt64(0, BigInt(value), true);
  return out;
}
function encodeErrorPayload(code, msg) {
  const b = new TextEncoder().encode(msg);
  const out = new Uint8Array(4 + 2 + b.length);
  writeU32LE(out, 0, code >>> 0);
  writeU16LE(out, 4, b.length);
  out.set(b, 6);
  return out;
}

let constructorCount = 0;
let callFramesSeen = 0;

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.instanceId = ++constructorCount;
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') this.onopen();
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    const id = readU32LE(bytes, 1);
    if (type === 0x01) {
      if (typeof this.onmessage !== 'function') return;
      if (this.instanceId === 1) {
        this.onmessage({ data: frame(0x03, 1, encodeErrorPayload(401, 'deny-first')) });
      } else {
        this.onmessage({ data: frame(0x02, 1, encodeWelcome()) });
      }
      return;
    }
    if (type === 0x10 && typeof this.onmessage === 'function') {
      callFramesSeen += 1;
      this.onmessage({ data: frame(0x11, id, encodeResult(7777)) });
    }
  }
  close() {
    this.readyState = FakeWebSocket.CLOSED;
    if (typeof this.onclose === 'function') this.onclose();
  }
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) throw new Error(`unexpected fetch path: ${String(url)}`);
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);

let firstError = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 31);
  throw new Error('expected first ws call to fail with handshake deny');
} catch (err) {
  firstError = String(err && err.message ? err.message : err);
}
if (!firstError.includes('remote handshake denied 401: deny-first')) {
  throw new Error(`unexpected first deny error: ${firstError}`);
}

const secondValue = await globalThis.__aivmRemoteCall('cap.remote', 'echo', 32);
if (secondValue !== 7777) {
  throw new Error(`unexpected second ws value after deny: ${String(secondValue)}`);
}
if (constructorCount < 2) {
  throw new Error(`expected reconnect after deny; websocket instances=${constructorCount}`);
}
if (callFramesSeen !== 1) {
  throw new Error(`expected one successful post-reconnect call frame, saw ${callFramesSeen}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_handshake_bad_id_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-handshake-bad-id.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws handshake-bad-id check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws handshake-bad-id check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws handshake-bad-id check missing required environment values');
}

function writeU16LE(arr, off, v) { arr[off] = v & 255; arr[off + 1] = (v >> 8) & 255; }
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function readU32LE(arr, off) {
  return (arr[off] | (arr[off + 1] << 8) | (arr[off + 2] << 16) | (arr[off + 3] << 24)) >>> 0;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}
function encodeWelcome() {
  const out = new Uint8Array(6);
  writeU16LE(out, 0, 1);
  writeU32LE(out, 2, 0);
  return out;
}
function encodeResult(value) {
  const out = new Uint8Array(8);
  const dv = new DataView(out.buffer);
  dv.setBigInt64(0, BigInt(value), true);
  return out;
}

let constructorCount = 0;

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.instanceId = ++constructorCount;
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') this.onopen();
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    const id = readU32LE(bytes, 1);
    if (type === 0x01) {
      if (typeof this.onmessage !== 'function') return;
      if (this.instanceId === 1) {
        this.onmessage({ data: frame(0x02, 2, encodeWelcome()) });
      } else {
        this.onmessage({ data: frame(0x02, 1, encodeWelcome()) });
      }
      return;
    }
    if (type === 0x10 && typeof this.onmessage === 'function') {
      this.onmessage({ data: frame(0x11, id, encodeResult(3131)) });
    }
  }
  close() {
    this.readyState = FakeWebSocket.CLOSED;
    if (typeof this.onclose === 'function') this.onclose();
  }
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) throw new Error(`unexpected fetch path: ${String(url)}`);
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);

let firstError = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 41);
  throw new Error('expected first ws call to fail with handshake bad id');
} catch (err) {
  firstError = String(err && err.message ? err.message : err);
}
if (!firstError.includes('remote unexpected handshake frame id 2')) {
  throw new Error(`unexpected first bad-id error: ${firstError}`);
}

const secondValue = await globalThis.__aivmRemoteCall('cap.remote', 'echo', 42);
if (secondValue !== 3131) {
  throw new Error(`unexpected second ws value after bad-id reconnect: ${String(secondValue)}`);
}
if (constructorCount < 2) {
  throw new Error(`expected reconnect after bad-id handshake; websocket instances=${constructorCount}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_reconnect_after_bad_handshake_type_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-reconnect-after-bad-handshake-type.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws reconnect-after-bad-handshake-type check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws reconnect-after-bad-handshake-type check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws reconnect-after-bad-handshake-type check missing required environment values');
}

function writeU16LE(arr, off, v) { arr[off] = v & 255; arr[off + 1] = (v >> 8) & 255; }
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function readU32LE(arr, off) {
  return (arr[off] | (arr[off + 1] << 8) | (arr[off + 2] << 16) | (arr[off + 3] << 24)) >>> 0;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}
function encodeWelcome() {
  const out = new Uint8Array(6);
  writeU16LE(out, 0, 1);
  writeU32LE(out, 2, 0);
  return out;
}
function encodeResult(value) {
  const out = new Uint8Array(8);
  const dv = new DataView(out.buffer);
  dv.setBigInt64(0, BigInt(value), true);
  return out;
}

let constructorCount = 0;

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.instanceId = ++constructorCount;
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') this.onopen();
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    const id = readU32LE(bytes, 1);
    if (type === 0x01) {
      if (typeof this.onmessage !== 'function') return;
      if (this.instanceId === 1) {
        this.onmessage({ data: frame(0x41, 1, new Uint8Array(0)) });
      } else {
        this.onmessage({ data: frame(0x02, 1, encodeWelcome()) });
      }
      return;
    }
    if (type === 0x10 && typeof this.onmessage === 'function') {
      this.onmessage({ data: frame(0x11, id, encodeResult(42424)) });
    }
  }
  close() {
    this.readyState = FakeWebSocket.CLOSED;
    if (typeof this.onclose === 'function') this.onclose();
  }
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) throw new Error(`unexpected fetch path: ${String(url)}`);
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);

let firstError = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 51);
  throw new Error('expected first ws call to fail with bad handshake type');
} catch (err) {
  firstError = String(err && err.message ? err.message : err);
}
if (!firstError.includes('remote unexpected handshake frame type 65')) {
  throw new Error(`unexpected first bad-type error: ${firstError}`);
}

const secondValue = await globalThis.__aivmRemoteCall('cap.remote', 'echo', 52);
if (secondValue !== 42424) {
  throw new Error(`unexpected second ws value after bad-type reconnect: ${String(secondValue)}`);
}
if (constructorCount < 2) {
  throw new Error(`expected reconnect after bad-type handshake; websocket instances=${constructorCount}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_reconnect_after_invalid_payload_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-reconnect-after-invalid-payload.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws reconnect-after-invalid-payload check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws reconnect-after-invalid-payload check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws reconnect-after-invalid-payload check missing required environment values');
}

function writeU16LE(arr, off, v) { arr[off] = v & 255; arr[off + 1] = (v >> 8) & 255; }
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function readU32LE(arr, off) {
  return (arr[off] | (arr[off + 1] << 8) | (arr[off + 2] << 16) | (arr[off + 3] << 24)) >>> 0;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}
function encodeWelcome() {
  const out = new Uint8Array(6);
  writeU16LE(out, 0, 1);
  writeU32LE(out, 2, 0);
  return out;
}
function encodeResult(value) {
  const out = new Uint8Array(8);
  const dv = new DataView(out.buffer);
  dv.setBigInt64(0, BigInt(value), true);
  return out;
}

let constructorCount = 0;

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.instanceId = ++constructorCount;
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') this.onopen();
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    const id = readU32LE(bytes, 1);
    if (type === 0x01) {
      if (typeof this.onmessage !== 'function') return;
      if (this.instanceId === 1) {
        this.onmessage({ data: 'not-binary' });
      } else {
        this.onmessage({ data: frame(0x02, 1, encodeWelcome()) });
      }
      return;
    }
    if (type === 0x10 && typeof this.onmessage === 'function') {
      this.onmessage({ data: frame(0x11, id, encodeResult(6161)) });
    }
  }
  close() {
    this.readyState = FakeWebSocket.CLOSED;
    if (typeof this.onclose === 'function') this.onclose();
  }
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) throw new Error(`unexpected fetch path: ${String(url)}`);
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);

let firstError = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 61);
  throw new Error('expected first ws call to fail with invalid payload');
} catch (err) {
  firstError = String(err && err.message ? err.message : err);
}
if (!firstError.includes('remote invalid websocket frame payload')) {
  throw new Error(`unexpected first invalid-payload error: ${firstError}`);
}

const secondValue = await globalThis.__aivmRemoteCall('cap.remote', 'echo', 62);
if (secondValue !== 6161) {
  throw new Error(`unexpected second ws value after invalid-payload reconnect: ${String(secondValue)}`);
}
if (constructorCount < 2) {
  throw new Error(`expected reconnect after invalid payload; websocket instances=${constructorCount}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_reconnect_after_short_frame_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-reconnect-after-short-frame.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws reconnect-after-short-frame check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws reconnect-after-short-frame check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws reconnect-after-short-frame check missing required environment values');
}

function writeU16LE(arr, off, v) { arr[off] = v & 255; arr[off + 1] = (v >> 8) & 255; }
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function readU32LE(arr, off) {
  return (arr[off] | (arr[off + 1] << 8) | (arr[off + 2] << 16) | (arr[off + 3] << 24)) >>> 0;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}
function encodeWelcome() {
  const out = new Uint8Array(6);
  writeU16LE(out, 0, 1);
  writeU32LE(out, 2, 0);
  return out;
}
function encodeResult(value) {
  const out = new Uint8Array(8);
  const dv = new DataView(out.buffer);
  dv.setBigInt64(0, BigInt(value), true);
  return out;
}

let constructorCount = 0;

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.instanceId = ++constructorCount;
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') this.onopen();
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    const id = readU32LE(bytes, 1);
    if (type === 0x01) {
      if (typeof this.onmessage !== 'function') return;
      if (this.instanceId === 1) {
        this.onmessage({ data: new Uint8Array([0x02, 0x01]).buffer });
      } else {
        this.onmessage({ data: frame(0x02, 1, encodeWelcome()) });
      }
      return;
    }
    if (type === 0x10 && typeof this.onmessage === 'function') {
      this.onmessage({ data: frame(0x11, id, encodeResult(7373)) });
    }
  }
  close() {
    this.readyState = FakeWebSocket.CLOSED;
    if (typeof this.onclose === 'function') this.onclose();
  }
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) throw new Error(`unexpected fetch path: ${String(url)}`);
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);

let firstError = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 71);
  throw new Error('expected first ws call to fail with short frame');
} catch (err) {
  firstError = String(err && err.message ? err.message : err);
}
if (!firstError.includes('remote invalid websocket frame payload')) {
  throw new Error(`unexpected first short-frame error: ${firstError}`);
}

const secondValue = await globalThis.__aivmRemoteCall('cap.remote', 'echo', 72);
if (secondValue !== 7373) {
  throw new Error(`unexpected second ws value after short-frame reconnect: ${String(secondValue)}`);
}
if (constructorCount < 2) {
  throw new Error(`expected reconnect after short frame; websocket instances=${constructorCount}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_reconnect_after_bad_result_payload_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-reconnect-after-bad-result-payload.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws reconnect-after-bad-result-payload check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws reconnect-after-bad-result-payload check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws reconnect-after-bad-result-payload check missing required environment values');
}

function writeU16LE(arr, off, v) { arr[off] = v & 255; arr[off + 1] = (v >> 8) & 255; }
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function readU32LE(arr, off) {
  return (arr[off] | (arr[off + 1] << 8) | (arr[off + 2] << 16) | (arr[off + 3] << 24)) >>> 0;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}
function encodeWelcome() {
  const out = new Uint8Array(6);
  writeU16LE(out, 0, 1);
  writeU32LE(out, 2, 0);
  return out;
}
function encodeResult(value) {
  const out = new Uint8Array(8);
  const dv = new DataView(out.buffer);
  dv.setBigInt64(0, BigInt(value), true);
  return out;
}

let constructorCount = 0;

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.instanceId = ++constructorCount;
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') this.onopen();
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    const id = readU32LE(bytes, 1);
    if (type === 0x01) {
      if (typeof this.onmessage === 'function') this.onmessage({ data: frame(0x02, 1, encodeWelcome()) });
      return;
    }
    if (type === 0x10 && typeof this.onmessage === 'function') {
      if (this.instanceId === 1) {
        // Invalid result payload (too short for int64 decode)
        this.onmessage({ data: frame(0x11, id, new Uint8Array([0x01, 0x02, 0x03])) });
      } else {
        this.onmessage({ data: frame(0x11, id, encodeResult(8181)) });
      }
      return;
    }
  }
  close() {
    this.readyState = FakeWebSocket.CLOSED;
    if (typeof this.onclose === 'function') this.onclose();
  }
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) throw new Error(`unexpected fetch path: ${String(url)}`);
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);

let firstError = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 81);
  throw new Error('expected first ws call to fail with bad result payload');
} catch (err) {
  firstError = String(err && err.message ? err.message : err);
}
if (!firstError.includes('remote invalid websocket frame payload')) {
  throw new Error(`unexpected first bad-result-payload error: ${firstError}`);
}

const secondValue = await globalThis.__aivmRemoteCall('cap.remote', 'echo', 82);
if (secondValue !== 8181) {
  throw new Error(`unexpected second ws value after bad-result-payload reconnect: ${String(secondValue)}`);
}
if (constructorCount < 2) {
  throw new Error(`expected reconnect after bad result payload; websocket instances=${constructorCount}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

run_web_runtime_ws_reconnect_after_bad_error_payload_check() {
  local label="$1"
  local web_root="$2"
  local main_js_path="${web_root}/main.js"
  local runtime_mjs_path="${web_root}/aivm-runtime-wasm32-web.mjs"
  local node_check_path="${TMP_DIR}/node-web-check-${label}-ws-reconnect-after-bad-error-payload.mjs"
  local app_fetch_path="./app.aibc1"

  if [[ ! -f "${main_js_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing main.js for ws reconnect-after-bad-error-payload check" >&2
    exit 1
  fi
  if [[ ! -f "${runtime_mjs_path}" ]]; then
    echo "wasm ${label} runtime mismatch: missing web runtime module for ws reconnect-after-bad-error-payload check" >&2
    exit 1
  fi

  cat > "${runtime_mjs_path}" <<'EOF'
export default async function createRuntime() {
  return {
    FS: { writeFile() {} },
    print: null,
    printErr: null,
    callMain() {}
  };
}
EOF

  cat > "${node_check_path}" <<'EOF'
import { pathToFileURL } from 'node:url';

const mainJsPath = process.env.AIVM_MAIN_JS;
const fetchPath = process.env.AIVM_FETCH_PATH;
if (!mainJsPath || !fetchPath) {
  throw new Error('node wasm ws reconnect-after-bad-error-payload check missing required environment values');
}

function writeU16LE(arr, off, v) { arr[off] = v & 255; arr[off + 1] = (v >> 8) & 255; }
function writeU32LE(arr, off, v) {
  arr[off] = v & 255;
  arr[off + 1] = (v >> 8) & 255;
  arr[off + 2] = (v >> 16) & 255;
  arr[off + 3] = (v >> 24) & 255;
}
function readU32LE(arr, off) {
  return (arr[off] | (arr[off + 1] << 8) | (arr[off + 2] << 16) | (arr[off + 3] << 24)) >>> 0;
}
function frame(type, id, payload) {
  const out = new Uint8Array(9 + payload.length);
  out[0] = type;
  writeU32LE(out, 1, id);
  writeU32LE(out, 5, payload.length);
  out.set(payload, 9);
  return out.buffer;
}
function encodeWelcome() {
  const out = new Uint8Array(6);
  writeU16LE(out, 0, 1);
  writeU32LE(out, 2, 0);
  return out;
}
function encodeResult(value) {
  const out = new Uint8Array(8);
  const dv = new DataView(out.buffer);
  dv.setBigInt64(0, BigInt(value), true);
  return out;
}

let constructorCount = 0;

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  constructor() {
    this.instanceId = ++constructorCount;
    this.readyState = FakeWebSocket.CONNECTING;
    this.onopen = null;
    this.onmessage = null;
    this.onerror = null;
    this.onclose = null;
    queueMicrotask(() => {
      this.readyState = FakeWebSocket.OPEN;
      if (typeof this.onopen === 'function') this.onopen();
    });
  }
  send(data) {
    const bytes = new Uint8Array(data);
    const type = bytes[0];
    const id = readU32LE(bytes, 1);
    if (type === 0x01) {
      if (typeof this.onmessage === 'function') this.onmessage({ data: frame(0x02, 1, encodeWelcome()) });
      return;
    }
    if (type === 0x10 && typeof this.onmessage === 'function') {
      if (this.instanceId === 1) {
        // Invalid error payload (too short for code + string decode)
        this.onmessage({ data: frame(0x12, id, new Uint8Array([0x01, 0x02, 0x03])) });
      } else {
        this.onmessage({ data: frame(0x11, id, encodeResult(9191)) });
      }
      return;
    }
  }
  close() {
    this.readyState = FakeWebSocket.CLOSED;
    if (typeof this.onclose === 'function') this.onclose();
  }
}

globalThis.WebSocket = FakeWebSocket;
globalThis.location = { hostname: 'localhost' };
globalThis.document = { getElementById() { return null; } };
globalThis.console = { log() {}, error() {} };
globalThis.fetch = async (url) => {
  if (String(url) !== fetchPath) throw new Error(`unexpected fetch path: ${String(url)}`);
  return { async arrayBuffer() { return new Uint8Array([1]).buffer; } };
};
globalThis.AIVM_REMOTE_MODE = 'ws';
globalThis.AiLang = { remote: {} };

await import(pathToFileURL(mainJsPath).href);

let firstError = '';
try {
  await globalThis.__aivmRemoteCall('cap.remote', 'echo', 91);
  throw new Error('expected first ws call to fail with bad error payload');
} catch (err) {
  firstError = String(err && err.message ? err.message : err);
}
if (!firstError.includes('remote invalid websocket frame payload')) {
  throw new Error(`unexpected first bad-error-payload error: ${firstError}`);
}

const secondValue = await globalThis.__aivmRemoteCall('cap.remote', 'echo', 92);
if (secondValue !== 9191) {
  throw new Error(`unexpected second ws value after bad-error-payload reconnect: ${String(secondValue)}`);
}
if (constructorCount < 2) {
  throw new Error(`expected reconnect after bad error payload; websocket instances=${constructorCount}`);
}
EOF

  AIVM_MAIN_JS="${main_js_path}" AIVM_FETCH_PATH="${app_fetch_path}" node "${node_check_path}"
}

if ! command -v wasmtime >/dev/null 2>&1; then
  echo "wasmtime is required to run wasm golden tests" >&2
  exit 1
fi
if ! command -v emcc >/dev/null 2>&1; then
  echo "emcc is required to build wasm runtime artifact for golden tests" >&2
  exit 1
fi
if ! command -v node >/dev/null 2>&1; then
  echo "node is required to run web runtime wasm profile checks" >&2
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
./tools/airun publish "${UI_WARN_CASE}" --target wasm32 --wasm-profile cli --out "${TMP_DIR}/ui-cli" >/dev/null 2>"${UI_CLI_WARN}"
./tools/airun publish "${UI_WARN_CASE}" --target wasm32 --wasm-profile spa --out "${TMP_DIR}/ui-spa" >/dev/null 2>"${UI_SPA_WARN}"
./tools/airun publish "${UI_WARN_CASE}" --target wasm32 --wasm-profile fullstack --out "${TMP_DIR}/ui-fullstack" >/dev/null 2>"${UI_FULLSTACK_WARN}"
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
if ! contains_fixed '__aivmUiCreateWindow' "${PUBLISH_SPA_DIR}/main.js"; then
  echo "wasm profile mismatch: spa publish did not emit ui createWindow bridge in main.js" >&2
  exit 1
fi
if ! contains_fixed '__aivmUiDrawLine' "${PUBLISH_SPA_DIR}/main.js" || ! contains_fixed '__aivmUiDrawEllipse' "${PUBLISH_SPA_DIR}/main.js"; then
  echo "wasm profile mismatch: spa publish did not emit ui line/ellipse bridges in main.js" >&2
  exit 1
fi
if ! contains_fixed '__aivmUiDrawPath' "${PUBLISH_SPA_DIR}/main.js" || ! contains_fixed '__aivmUiDrawImage' "${PUBLISH_SPA_DIR}/main.js"; then
  echo "wasm profile mismatch: spa publish did not emit ui path/image bridges in main.js" >&2
  exit 1
fi
if ! contains_fixed 'AIVM_HOST_STDIN_READ' "${PUBLISH_SPA_DIR}/main.js"; then
  echo "wasm profile mismatch: spa publish did not emit optional host-stdin callback hook in main.js" >&2
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
if ! contains_fixed '__aivmUiCreateWindow' "${PUBLISH_FULLSTACK_DIR}/www/main.js"; then
  echo "wasm profile mismatch: fullstack publish did not emit ui createWindow bridge in www/main.js" >&2
  exit 1
fi
if ! contains_fixed '__aivmUiDrawLine' "${PUBLISH_FULLSTACK_DIR}/www/main.js" || ! contains_fixed '__aivmUiDrawEllipse' "${PUBLISH_FULLSTACK_DIR}/www/main.js"; then
  echo "wasm profile mismatch: fullstack publish did not emit ui line/ellipse bridges in www/main.js" >&2
  exit 1
fi
if ! contains_fixed '__aivmUiDrawPath' "${PUBLISH_FULLSTACK_DIR}/www/main.js" || ! contains_fixed '__aivmUiDrawImage' "${PUBLISH_FULLSTACK_DIR}/www/main.js"; then
  echo "wasm profile mismatch: fullstack publish did not emit ui path/image bridges in www/main.js" >&2
  exit 1
fi
if ! contains_fixed 'AIVM_HOST_STDIN_READ' "${PUBLISH_FULLSTACK_DIR}/www/main.js"; then
  echo "wasm profile mismatch: fullstack publish did not emit optional host-stdin callback hook in www/main.js" >&2
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

run_web_runtime_js_mode_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_js_mode_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_js_mode_missing_adapter_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_js_mode_missing_adapter_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_invalid_mode_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_invalid_mode_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_mode_call_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_mode_call_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_mode_deny_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_mode_deny_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_mode_call_error_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_mode_call_error_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_mode_socket_error_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_mode_socket_error_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_default_endpoint_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_default_endpoint_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_unexpected_call_frame_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_unexpected_call_frame_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_unexpected_handshake_frame_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_unexpected_handshake_frame_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_pending_close_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_pending_close_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_pending_error_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_pending_error_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_unknown_id_ignored_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_unknown_id_ignored_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_handshake_close_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_handshake_close_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_reconnect_after_error_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_reconnect_after_error_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_reconnect_after_deny_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_reconnect_after_deny_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_handshake_bad_id_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_handshake_bad_id_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_reconnect_after_bad_handshake_type_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_reconnect_after_bad_handshake_type_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_reconnect_after_invalid_payload_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_reconnect_after_invalid_payload_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_reconnect_after_short_frame_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_reconnect_after_short_frame_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_reconnect_after_bad_result_payload_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_reconnect_after_bad_result_payload_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"
run_web_runtime_ws_reconnect_after_bad_error_payload_check "spa" "${PUBLISH_SPA_DIR}"
run_web_runtime_ws_reconnect_after_bad_error_payload_check "fullstack" "${PUBLISH_FULLSTACK_DIR}/www"

FULLSTACK_HOST_PORT="$((19000 + ($$ % 1000)))"
(
  cd "${PUBLISH_FULLSTACK_DIR}"
  PORT="${FULLSTACK_HOST_PORT}" "./${EXPECTED_FULLSTACK_APP_BIN}" >"${FULLSTACK_HOST_STDOUT}" 2>"${FULLSTACK_HOST_STDERR}"
) &
fullstack_host_pid=$!
host_exit_observed=0
for _ in {1..20}; do
  if ! kill -0 "${fullstack_host_pid}" >/dev/null 2>&1; then
    host_exit_observed=1
    break
  fi
  sleep 0.1
done
if [[ "${host_exit_observed}" == "1" ]]; then
  set +e
  wait "${fullstack_host_pid}"
  fullstack_host_rc=$?
  set -e
  if [[ ${fullstack_host_rc} -ne 2 ]]; then
    echo "wasm fullstack host mismatch: expected launcher bind/listen failure exit 2, got ${fullstack_host_rc}" >&2
    exit 1
  fi
  if ! contains_fixed 'Err#err1(code=RUN001 message="Failed to bind/listen fullstack server socket." nodeId=publish)' "${FULLSTACK_HOST_STDERR}"; then
    echo "wasm fullstack host mismatch: expected deterministic bind/listen stderr diagnostic" >&2
    exit 1
  fi
else
  if ! contains_fixed '[fullstack] serving static client from' "${FULLSTACK_HOST_STDOUT}"; then
    echo "wasm fullstack host mismatch: expected stdout serving banner when launcher stays alive" >&2
    exit 1
  fi
  kill "${fullstack_host_pid}" >/dev/null 2>&1 || true
  wait "${fullstack_host_pid}" 2>/dev/null || true
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
if ! contains_fixed "Warn#warn1(code=WASM001 message=\"sys.ui.drawRect is not available on wasm profile 'cli'" "${UI_CLI_WARN}"; then
  echo "wasm cli warning mismatch: expected WASM001 warning for sys.ui.drawRect" >&2
  exit 1
fi
if contains_fixed "Warn#warn1(code=WASM001 message=\"sys.ui.drawRect is not available on wasm profile 'spa'" "${UI_SPA_WARN}"; then
  echo "wasm spa warning mismatch: unexpected WASM001 warning for sys.ui.drawRect" >&2
  exit 1
fi
if contains_fixed "Warn#warn1(code=WASM001 message=\"sys.ui.drawRect is not available on wasm profile 'fullstack'" "${UI_FULLSTACK_WARN}"; then
  echo "wasm fullstack warning mismatch: unexpected WASM001 warning for sys.ui.drawRect" >&2
  exit 1
fi

echo "wasm golden profiles: PASS (cli/spa/fullstack)"
