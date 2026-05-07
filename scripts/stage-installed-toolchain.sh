#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INSTALL_ROOT="${AILANG_INSTALL_ROOT:-${HOME}/.ailang}"
TOOLS_DIR="${ROOT_DIR}/tools"

find_project_toolchain() {
  if [[ "${AILANG_IGNORE_PROJECT_TOOLCHAIN:-0}" == "1" ]]; then
    return 0
  fi
  local dir="${ROOT_DIR}"
  while :; do
    if [[ -f "${dir}/ailang-toolchain.toml" ]]; then
      sed -n 's/^[[:space:]]*version[[:space:]]*=[[:space:]]*"\([^"]*\)".*/\1/p' "${dir}/ailang-toolchain.toml" | head -n 1
      return 0
    fi
    if [[ -f "${dir}/.ailang-toolchain" ]]; then
      sed -n '1{s/^[[:space:]]*//;s/[[:space:]]*$//;p;}' "${dir}/.ailang-toolchain"
      return 0
    fi
    [[ "${dir}" == "/" ]] && return 0
    dir="$(dirname "${dir}")"
  done
}

resolve_toolchain_root() {
  local selected="${AILANG_TOOLCHAIN:-$(find_project_toolchain)}"
  if [[ -n "${selected}" ]]; then
    if [[ "${selected}" == "local" ]]; then
      printf '%s\n' "${INSTALL_ROOT}/local"
    else
      printf '%s\n' "${INSTALL_ROOT}/toolchains/${selected}"
    fi
    return 0
  fi
  printf '%s\n' "${INSTALL_ROOT}/current"
}

copy_executable() {
  local src="$1"
  local dst="$2"
  cp "${src}" "${dst}"
  chmod +x "${dst}"
}

SDK_ROOT="$(resolve_toolchain_root)"
SDK_BIN="${SDK_ROOT}/bin"

if [[ ! -d "${SDK_ROOT}" ]]; then
  echo "selected AiLang SDK is not installed: ${SDK_ROOT}" >&2
  echo "Install a versioned SDK or run scripts/update-local-toolchain.sh for ~/.ailang/local." >&2
  exit 1
fi

if [[ ! -x "${SDK_BIN}/ailang" ]]; then
  echo "selected AiLang SDK is missing bin/ailang: ${SDK_ROOT}" >&2
  exit 1
fi

mkdir -p "${TOOLS_DIR}"
copy_executable "${SDK_BIN}/ailang" "${TOOLS_DIR}/ailang"

if [[ -x "${SDK_BIN}/airun" ]]; then
  copy_executable "${SDK_BIN}/airun" "${TOOLS_DIR}/airun"
else
  copy_executable "${SDK_BIN}/ailang" "${TOOLS_DIR}/airun"
fi

if [[ -x "${SDK_BIN}/aivm-runtime" ]]; then
  copy_executable "${SDK_BIN}/aivm-runtime" "${TOOLS_DIR}/aivm-runtime"
fi

if [[ -x "${SDK_BIN}/aos_frontend" ]]; then
  copy_executable "${SDK_BIN}/aos_frontend" "${TOOLS_DIR}/aos_frontend"
fi

cat <<EOF
Staged AiLang tools from installed SDK:
  ${SDK_ROOT}
EOF
