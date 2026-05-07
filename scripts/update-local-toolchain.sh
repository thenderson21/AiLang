#!/usr/bin/env bash
set -euo pipefail

AILANG_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKSPACE_DIR="$(cd "${AILANG_DIR}/.." && pwd)"
INSTALL_ROOT="${AILANG_INSTALL_ROOT:-${HOME}/.ailang}"
SDK_NAME="${AILANG_LOCAL_SDK_NAME:-local}"
if [[ "${SDK_NAME}" == "local" ]]; then
  SDK_ROOT="${INSTALL_ROOT}/local"
else
  SDK_ROOT="${INSTALL_ROOT}/toolchains/${SDK_NAME}"
fi
AIVM_DIR="${AIVM_REPO_DIR:-${WORKSPACE_DIR}/AiVM}"
AIVECTRA_DIR="${AIVECTRA_REPO_DIR:-${WORKSPACE_DIR}/AiVectra}"
BUILD_WASM="${AILANG_LOCAL_BUILD_WASM:-auto}"

usage() {
  cat <<'EOF'
Usage: scripts/update-local-toolchain.sh [--root <path>] [--sdk-name <name>] [--workspace <path>] [--with-wasm|--no-wasm]

Builds the local workspace toolchain and stages it into:
  ~/.ailang/local

The script also updates:
  ~/.ailang/bin/{ailang,airun,aivm,aivectra}

It does not update ~/.ailang/current. Project-local selectors or
AILANG_TOOLCHAIN=local opt into this mutable SDK.

Environment:
  AILANG_INSTALL_ROOT       Install root. Default: ~/.ailang
  AILANG_LOCAL_SDK_NAME     Local toolchain slot name. Default: local
  AILANG_LOCAL_BUILD_WASM   auto, 1, or 0. Default: auto
  AIVM_REPO_DIR             AiVM checkout. Default: ../AiVM
  AIVECTRA_REPO_DIR         AiVectra checkout. Default: ../AiVectra
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --root)
      INSTALL_ROOT="${2:-}"
      shift 2
      ;;
    --sdk-name)
      SDK_NAME="${2:-}"
      shift 2
      ;;
    --workspace)
      WORKSPACE_DIR="$(cd "${2:-}" && pwd)"
      AIVM_DIR="${AIVM_REPO_DIR:-${WORKSPACE_DIR}/AiVM}"
      AIVECTRA_DIR="${AIVECTRA_REPO_DIR:-${WORKSPACE_DIR}/AiVectra}"
      shift 2
      ;;
    --with-wasm)
      BUILD_WASM=1
      shift
      ;;
    --no-wasm)
      BUILD_WASM=0
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ "${SDK_NAME}" == "local" ]]; then
  SDK_ROOT="${INSTALL_ROOT}/local"
else
  SDK_ROOT="${INSTALL_ROOT}/toolchains/${SDK_NAME}"
fi
BIN_DIR="${SDK_ROOT}/bin"
ARTIFACTS_DIR="${SDK_ROOT}/.artifacts"
MANIFEST_DIR="${SDK_ROOT}/manifests"

detect_rid() {
  local os arch platform cpu
  os="$(uname -s)"
  arch="$(uname -m)"
  case "${os}" in
    Darwin) platform="osx" ;;
    Linux) platform="linux" ;;
    *) echo "unsupported OS for local toolchain: ${os}" >&2; exit 1 ;;
  esac
  case "${arch}" in
    arm64|aarch64) cpu="arm64" ;;
    x86_64|amd64) cpu="x64" ;;
    *) echo "unsupported architecture for local toolchain: ${arch}" >&2; exit 1 ;;
  esac
  printf '%s-%s\n' "${platform}" "${cpu}"
}

write_shim() {
  local name="$1"
  local target="$2"
  local path="${INSTALL_ROOT}/bin/${name}"
  cat > "${path}" <<EOF
#!/usr/bin/env sh
set -eu
ROOT="\$(CDPATH= cd -- "\$(dirname -- "\$0")/.." && pwd)"
find_project_toolchain() {
  dir="\$(pwd)"
  while :; do
    if [ -f "\$dir/ailang-toolchain.toml" ]; then
      sed -n 's/^[[:space:]]*version[[:space:]]*=[[:space:]]*"\([^"]*\)".*/\1/p' "\$dir/ailang-toolchain.toml" | head -n 1
      return 0
    fi
    if [ -f "\$dir/.ailang-toolchain" ]; then
      sed -n '1{s/^[[:space:]]*//;s/[[:space:]]*$//;p;}' "\$dir/.ailang-toolchain"
      return 0
    fi
    [ "\$dir" = "/" ] && return 0
    dir="\$(dirname -- "\$dir")"
  done
}
SELECTED="\${AILANG_TOOLCHAIN:-\$(find_project_toolchain)}"
if [ -n "\$SELECTED" ]; then
  if [ "\$SELECTED" = "local" ] && [ -d "\$ROOT/local" ]; then
    CURRENT="\$ROOT/local"
  else
    CURRENT="\$ROOT/toolchains/\$SELECTED"
  fi
  if [ ! -d "\$CURRENT" ]; then
    echo "selected AiLang toolchain is not installed: \$SELECTED" >&2
    echo "expected: \$CURRENT" >&2
    exit 127
  fi
else
  CURRENT="\$ROOT/current"
fi
if [ -x "\$CURRENT/bin/${target}" ]; then
  exec "\$CURRENT/bin/${target}" "\$@"
fi
if [ -x "\$CURRENT/${target}" ]; then
  exec "\$CURRENT/${target}" "\$@"
fi
if [ "${target}" = "ailang" ] && [ -x "\$CURRENT/bin/airun" ]; then
  exec "\$CURRENT/bin/airun" "\$@"
fi
echo "missing installed executable: ${target}" >&2
exit 127
EOF
  chmod +x "${path}"
}

copy_if_exists() {
  local src="$1"
  local dst="$2"
  if [[ -e "${src}" ]]; then
    mkdir -p "$(dirname "${dst}")"
    cp -R "${src}" "${dst}"
  fi
}

RID="$(detect_rid)"
TMP_ROOT="${SDK_ROOT}.tmp.$$"

rm -rf "${TMP_ROOT}"
mkdir -p "${TMP_ROOT}/bin" "${TMP_ROOT}/lib" "${TMP_ROOT}/include" "${TMP_ROOT}/sdk" "${TMP_ROOT}/compiler" "${TMP_ROOT}/std" "${TMP_ROOT}/sys" "${TMP_ROOT}/manifests" "${TMP_ROOT}/.artifacts"

echo "building AiVM from ${AIVM_DIR}..."
if [[ -d "${AIVM_DIR}" ]]; then
  (cd "${AIVM_DIR}" && ./build.sh host)
  AIVM_BUILD_DIR="${AIVM_DIR}/.tmp/aivm-c-build-native"
  copy_if_exists "${AIVM_BUILD_DIR}/aivm" "${TMP_ROOT}/bin/aivm"
  copy_if_exists "${AIVM_BUILD_DIR}/libaivm_core.a" "${TMP_ROOT}/lib/libaivm_core.a"
  copy_if_exists "${AIVM_DIR}/native/include" "${TMP_ROOT}/include/aivm"
else
  echo "warning: AiVM checkout not found: ${AIVM_DIR}" >&2
fi

echo "building AiLang from ${AILANG_DIR}..."
if [[ -d "${AIVM_DIR}/native" ]]; then
  (cd "${AILANG_DIR}" && AIVM_C_SOURCE_DIR="${AIVM_DIR}/native" ./build.sh host)
else
  (cd "${AILANG_DIR}" && ./build.sh host)
fi

copy_if_exists "${AILANG_DIR}/tools/ailang" "${TMP_ROOT}/bin/ailang"
copy_if_exists "${AILANG_DIR}/tools/airun" "${TMP_ROOT}/bin/airun"
copy_if_exists "${AILANG_DIR}/tools/aivm-runtime" "${TMP_ROOT}/bin/aivm-runtime"
copy_if_exists "${AILANG_DIR}/tools/aos_frontend" "${TMP_ROOT}/bin/aos_frontend"
copy_if_exists "${AILANG_DIR}/src/compiler/." "${TMP_ROOT}/compiler/"
copy_if_exists "${AILANG_DIR}/src/std/." "${TMP_ROOT}/std/"
copy_if_exists "${AILANG_DIR}/src/std/." "${TMP_ROOT}/sys/"
copy_if_exists "${AILANG_DIR}/templates" "${TMP_ROOT}/templates"
copy_if_exists "${AILANG_DIR}/Docs" "${TMP_ROOT}/sdk/AiLangDocs"

if [[ ! -x "${TMP_ROOT}/bin/ailang" && -x "${TMP_ROOT}/bin/airun" ]]; then
  cp "${TMP_ROOT}/bin/airun" "${TMP_ROOT}/bin/ailang"
fi

if [[ "${BUILD_WASM}" == "1" || ( "${BUILD_WASM}" == "auto" && -n "$(command -v emcc || true)" ) ]]; then
  echo "building AiLang wasm runtime artifacts..."
  (cd "${AILANG_DIR}" && ./build.sh wasm)
  mkdir -p "${TMP_ROOT}/.artifacts/aivm-wasm32"
  copy_if_exists "${AILANG_DIR}/.artifacts/aivm-wasm32/aivm-runtime-wasm32.wasm" "${TMP_ROOT}/.artifacts/aivm-wasm32/aivm-runtime-wasm32.wasm"
  copy_if_exists "${AILANG_DIR}/.artifacts/aivm-wasm32/aivm-runtime-wasm32-web.wasm" "${TMP_ROOT}/.artifacts/aivm-wasm32/aivm-runtime-wasm32-web.wasm"
  copy_if_exists "${AILANG_DIR}/.artifacts/aivm-wasm32/aivm-runtime-wasm32-web.mjs" "${TMP_ROOT}/.artifacts/aivm-wasm32/aivm-runtime-wasm32-web.mjs"
else
  echo "skipping wasm runtime build; pass --with-wasm to require it."
fi

if [[ -d "${AIVECTRA_DIR}" ]]; then
  echo "staging AiVectra from ${AIVECTRA_DIR}..."
  copy_if_exists "${AIVECTRA_DIR}/scripts/aivectra" "${TMP_ROOT}/bin/aivectra"
  copy_if_exists "${AIVECTRA_DIR}/src/AiVectra" "${TMP_ROOT}/sdk/AiVectra"
  copy_if_exists "${AIVECTRA_DIR}/src/AiVectra.Cli" "${TMP_ROOT}/sdk/AiVectra.Cli"
fi

find "${TMP_ROOT}/bin" -maxdepth 1 -type f -exec chmod +x {} +

if [[ ! -x "${TMP_ROOT}/bin/ailang" ]]; then
  echo "error: local toolchain is missing ailang" >&2
  exit 1
fi
if [[ ! -x "${TMP_ROOT}/bin/aivm" ]]; then
  echo "warning: local toolchain is missing aivm" >&2
fi

cat > "${TMP_ROOT}/manifests/local.toml" <<EOF
suite_version = "local"
channel = "local"
rid = "${RID}"
created_by = "scripts/update-local-toolchain.sh"

[[component]]
name = "AiLang"
version = "local"
path = "${AILANG_DIR}"

[[component]]
name = "AiVM"
version = "local"
path = "${AIVM_DIR}"

[[component]]
name = "AiVectra"
version = "local"
path = "${AIVECTRA_DIR}"
EOF

rm -rf "${SDK_ROOT}"
mv "${TMP_ROOT}" "${SDK_ROOT}"
mkdir -p "${INSTALL_ROOT}/bin" "${INSTALL_ROOT}/toolchains"
write_shim ailang ailang
write_shim airun airun
write_shim aivm aivm
write_shim aivectra aivectra

cat <<EOF
Updated local AiLangCore toolchain:
  ${SDK_ROOT}

Add this to PATH:
  export PATH="${INSTALL_ROOT}/bin:\$PATH"

Use the local SDK per project:
  scripts/select-toolchain.sh local <project-dir>

Smoke checks:
  ailang --version
  aivm --help
EOF
