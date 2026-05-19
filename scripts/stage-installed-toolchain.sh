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
LEGACY_BOOTSTRAP="${AILANG_ALLOW_LEGACY_BOOTSTRAP_SDK:-0}"

if [[ ! -d "${SDK_ROOT}" ]]; then
  echo "selected AiLang SDK is not installed: ${SDK_ROOT}" >&2
  echo "Install a versioned SDK or run scripts/update-local-toolchain.sh for ~/.ailang/local." >&2
  exit 1
fi

resolve_sdk_executable() {
  local name="$1"
  local canonical="${SDK_BIN}/${name}"
  if [[ -x "${canonical}" ]]; then
    printf '%s\n' "${canonical}"
    return 0
  fi
  if [[ "${LEGACY_BOOTSTRAP}" == "1" && -x "${SDK_ROOT}/${name}" ]]; then
    printf '%s\n' "${SDK_ROOT}/${name}"
    return 0
  fi
  return 1
}

AILANG_BIN="$(resolve_sdk_executable ailang || true)"
if [[ -z "${AILANG_BIN}" ]]; then
  echo "selected AiLang SDK is missing bin/ailang: ${SDK_ROOT}" >&2
  if [[ "${LEGACY_BOOTSTRAP}" != "1" ]]; then
    echo "Set AILANG_ALLOW_LEGACY_BOOTSTRAP_SDK=1 only when bootstrapping from a pre-bin-layout alpha SDK." >&2
  fi
  exit 1
fi

mkdir -p "${TOOLS_DIR}"
copy_executable "${AILANG_BIN}" "${TOOLS_DIR}/ailang"
rm -f "${TOOLS_DIR}/ailang" "${TOOLS_DIR}/ailang.exe"

RUNTIME_BIN="$(resolve_sdk_executable aivm-runtime || true)"
if [[ -n "${RUNTIME_BIN}" ]]; then
  copy_executable "${RUNTIME_BIN}" "${TOOLS_DIR}/aivm-runtime"
fi

FRONTEND_BIN="$(resolve_sdk_executable aos_frontend || true)"
if [[ -n "${FRONTEND_BIN}" ]]; then
  copy_executable "${FRONTEND_BIN}" "${TOOLS_DIR}/aos_frontend"
fi

if [[ -d "${SDK_ROOT}/.artifacts" ]]; then
  mkdir -p "${ROOT_DIR}/.artifacts"
  cp -R "${SDK_ROOT}/.artifacts"/. "${ROOT_DIR}/.artifacts"/
fi

cat <<EOF
Staged AiLang tools from installed SDK:
  ${SDK_ROOT}
EOF
