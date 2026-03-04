#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE_PATH="${ROOT_DIR}/src/AiCLI/native/airun.c"
NATIVE_INCLUDE="${ROOT_DIR}/src/AiVM.Core/native/include"
NATIVE_SRC_DIR="${ROOT_DIR}/src/AiVM.Core/native/src"
UNAME_S="$(uname -s)"
UNAME_M="$(uname -m)"
HOST_WRAPPER_PATH="${ROOT_DIR}/tools/airun"

case "${UNAME_S}" in
  Darwin) HOST_PLATFORM="osx" ;;
  Linux) HOST_PLATFORM="linux" ;;
  *)
    echo "build-airun.sh supports only macOS/Linux (got ${UNAME_S})" >&2
    exit 1
    ;;
esac

case "${UNAME_M}" in
  arm64|aarch64) HOST_ARCH="arm64" ;;
  x86_64|amd64) HOST_ARCH="x64" ;;
  *)
    echo "unsupported CPU architecture for airun wrapper build: ${UNAME_M}" >&2
    exit 1
    ;;
esac

TARGET_PLATFORM="${AIVM_AIRUN_PLATFORM:-${HOST_PLATFORM}}"
TARGET_ARCH="${AIVM_AIRUN_ARCH:-${HOST_ARCH}}"

if [[ "${TARGET_PLATFORM}" != "osx" && "${TARGET_PLATFORM}" != "linux" ]]; then
  echo "unsupported AIVM_AIRUN_PLATFORM: ${TARGET_PLATFORM}" >&2
  exit 1
fi
if [[ "${TARGET_ARCH}" != "x64" && "${TARGET_ARCH}" != "arm64" ]]; then
  echo "unsupported AIVM_AIRUN_ARCH: ${TARGET_ARCH}" >&2
  exit 1
fi

OUT_DIR="${ROOT_DIR}/.artifacts/airun-${TARGET_PLATFORM}-${TARGET_ARCH}"
WRAPPER_PATH="${OUT_DIR}/airun"

"${ROOT_DIR}/scripts/build-frontend.sh"

mkdir -p "${OUT_DIR}"

CC_BIN="cc"
CC_EXTRA=()
if [[ "${TARGET_PLATFORM}" == "osx" ]]; then
  CC_BIN="clang"
  if [[ "${TARGET_ARCH}" == "x64" ]]; then
    CC_EXTRA=(-arch x86_64)
  else
    CC_EXTRA=(-arch arm64)
  fi
elif [[ "${TARGET_PLATFORM}" == "linux" && "${TARGET_ARCH}" == "arm64" && "${HOST_ARCH}" == "x64" ]]; then
  if command -v aarch64-linux-gnu-gcc >/dev/null 2>&1; then
    CC_BIN="aarch64-linux-gnu-gcc"
  fi
fi

"${CC_BIN}" -std=c17 -Wall -Wextra -Werror -O2 "${CC_EXTRA[@]}" \
  -I "${NATIVE_INCLUDE}" \
  "${SOURCE_PATH}" \
  "${NATIVE_SRC_DIR}/aivm_types.c" \
  "${NATIVE_SRC_DIR}/aivm_vm.c" \
  "${NATIVE_SRC_DIR}/aivm_program.c" \
  "${NATIVE_SRC_DIR}/sys/aivm_syscall.c" \
  "${NATIVE_SRC_DIR}/sys/aivm_syscall_contracts.c" \
  "${NATIVE_SRC_DIR}/aivm_parity.c" \
  "${NATIVE_SRC_DIR}/aivm_runtime.c" \
  "${NATIVE_SRC_DIR}/aivm_c_api.c" \
  "${NATIVE_SRC_DIR}/remote/aivm_remote_channel.c" \
  "${NATIVE_SRC_DIR}/remote/aivm_remote_session.c" \
  -o "${WRAPPER_PATH}"
chmod +x "${WRAPPER_PATH}"

if [[ "${TARGET_PLATFORM}" == "${HOST_PLATFORM}" && "${TARGET_ARCH}" == "${HOST_ARCH}" ]]; then
  cp "${WRAPPER_PATH}" "${HOST_WRAPPER_PATH}"
  chmod +x "${HOST_WRAPPER_PATH}"
fi
