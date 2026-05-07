#!/usr/bin/env bash

resolve_aivm_native_dir() {
  local root_dir="$1"
  local explicit_dir="${AIVM_C_SOURCE_DIR:-}"
  local sibling_dir="${root_dir}/../AiVM/native"

  if [[ -n "${explicit_dir}" ]]; then
    printf '%s\n' "${explicit_dir}"
    return 0
  fi
  if [[ "${AILANG_ALLOW_SIBLING_AIVM_SOURCE:-0}" == "1" && -d "${sibling_dir}" ]]; then
    printf '%s\n' "${sibling_dir}"
    return 0
  fi
  printf '%s\n' ""
}

require_aivm_native_dir() {
  local root_dir="$1"
  local native_dir
  native_dir="$(resolve_aivm_native_dir "${root_dir}")"
  if [[ -z "${native_dir}" || ! -d "${native_dir}" ]]; then
    echo "AiVM native source directory is not configured." >&2
    echo "Set AIVM_C_SOURCE_DIR for source-level VM tests." >&2
    echo "For workspace migration only, set AILANG_ALLOW_SIBLING_AIVM_SOURCE=1 to use ../AiVM/native." >&2
    return 1
  fi
  printf '%s\n' "${native_dir}"
}

resolve_aivm_bin() {
  local install_root="${AILANG_INSTALL_ROOT:-${HOME}/.ailang}"
  local selected=""
  local current_dir

  if [[ -n "${AIVM_BIN:-}" ]]; then
    printf '%s\n' "${AIVM_BIN}"
    return 0
  fi

  if [[ -n "${AILANG_TOOLCHAIN:-}" ]]; then
    selected="${AILANG_TOOLCHAIN}"
  elif [[ "${AILANG_IGNORE_PROJECT_TOOLCHAIN:-0}" != "1" ]]; then
    current_dir="${PWD}"
    while [[ -n "${current_dir}" ]]; do
      if [[ -f "${current_dir}/ailang-toolchain.toml" ]]; then
        selected="$(sed -n 's/^[[:space:]]*version[[:space:]]*=[[:space:]]*"\([^"]*\)".*/\1/p' "${current_dir}/ailang-toolchain.toml" | head -n 1)"
        break
      fi
      if [[ -f "${current_dir}/.ailang-toolchain" ]]; then
        selected="$(sed -n '1{s/^[[:space:]]*//;s/[[:space:]]*$//;p;}' "${current_dir}/.ailang-toolchain")"
        break
      fi
      [[ "${current_dir}" == "/" ]] && break
      current_dir="$(dirname "${current_dir}")"
    done
  fi

  if [[ -n "${selected}" ]]; then
    if [[ "${selected}" == "local" && -x "${install_root}/local/bin/aivm" ]]; then
      printf '%s\n' "${install_root}/local/bin/aivm"
      return 0
    fi
    if [[ -x "${install_root}/toolchains/${selected}/bin/aivm" ]]; then
      printf '%s\n' "${install_root}/toolchains/${selected}/bin/aivm"
      return 0
    fi
  fi

  if [[ -x "${install_root}/current/bin/aivm" ]]; then
    printf '%s\n' "${install_root}/current/bin/aivm"
    return 0
  fi
  if command -v aivm >/dev/null 2>&1; then
    command -v aivm
    return 0
  fi
  printf '%s\n' ""
}

require_aivm_bin() {
  local bin
  bin="$(resolve_aivm_bin)"
  if [[ -z "${bin}" || ! -x "${bin}" ]]; then
    echo "AiVM executable not found." >&2
    echo "Install AiVM with the AiLangCore installer or set AIVM_BIN." >&2
    return 1
  fi
  printf '%s\n' "${bin}"
}
