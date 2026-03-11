#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE_PATH="${ROOT_DIR}/src/AiCLI/native/airun.c"
NATIVE_INCLUDE="${ROOT_DIR}/src/AiVM.Core/native/include"
NATIVE_SRC_DIR="${ROOT_DIR}/src/AiVM.Core/native/src"
NATIVE_UI_HOST_SRC="${ROOT_DIR}/src/AiCLI/native/airun_ui_host_macos.m"
NATIVE_UI_HOST_LINUX_SRC="${ROOT_DIR}/src/AiCLI/native/airun_ui_host_linux.c"
NATIVE_UI_HOST_WINDOWS_SRC="${ROOT_DIR}/src/AiCLI/native/airun_ui_host_windows.c"
NATIVE_UI_HOST_UNAVAILABLE_SRC="${ROOT_DIR}/src/AiCLI/native/airun_ui_host_unavailable.c"
UNAME_S="$(uname -s)"
UNAME_M="$(uname -m)"
HOST_WRAPPER_PATH="${ROOT_DIR}/tools/airun"

case "${UNAME_S}" in
  Darwin) HOST_PLATFORM="osx" ;;
  Linux) HOST_PLATFORM="linux" ;;
  MINGW*|MSYS*|CYGWIN*) HOST_PLATFORM="windows" ;;
  *)
    echo "build-airun.sh supports only macOS/Linux/Windows (got ${UNAME_S})" >&2
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

if [[ "${TARGET_PLATFORM}" != "osx" && "${TARGET_PLATFORM}" != "linux" && "${TARGET_PLATFORM}" != "windows" ]]; then
  echo "unsupported AIVM_AIRUN_PLATFORM: ${TARGET_PLATFORM}" >&2
  exit 1
fi
if [[ "${TARGET_ARCH}" != "x64" && "${TARGET_ARCH}" != "arm64" ]]; then
  echo "unsupported AIVM_AIRUN_ARCH: ${TARGET_ARCH}" >&2
  exit 1
fi

OUT_DIR="${ROOT_DIR}/.artifacts/airun-${TARGET_PLATFORM}-${TARGET_ARCH}"
AIRUN_BIN_NAME="airun"
RUNTIME_BIN_NAME="aivm-runtime"
if [[ "${TARGET_PLATFORM}" == "windows" ]]; then
  AIRUN_BIN_NAME="airun.exe"
  RUNTIME_BIN_NAME="aivm-runtime.exe"
fi
WRAPPER_PATH="${OUT_DIR}/${AIRUN_BIN_NAME}"
RUNTIME_PATH="${OUT_DIR}/${RUNTIME_BIN_NAME}"

"${ROOT_DIR}/scripts/build-frontend.sh"

mkdir -p "${OUT_DIR}"

CC_BIN="cc"
CC_EXTRA=()
LD_EXTRA=()
UI_HOST_SRC="${NATIVE_UI_HOST_UNAVAILABLE_SRC}"
if [[ "${TARGET_PLATFORM}" == "osx" ]]; then
  CC_BIN="clang"
  if [[ "${TARGET_ARCH}" == "x64" ]]; then
    CC_EXTRA=(-arch x86_64)
  else
    CC_EXTRA=(-arch arm64)
  fi
  UI_HOST_SRC="${NATIVE_UI_HOST_SRC}"
  LD_EXTRA=(-framework AppKit -framework Foundation -framework Security -framework CoreFoundation -framework CoreGraphics -framework ImageIO -framework CFNetwork)
elif [[ "${TARGET_PLATFORM}" == "linux" && "${TARGET_ARCH}" == "arm64" && "${HOST_ARCH}" == "x64" ]]; then
  UI_HOST_SRC="${NATIVE_UI_HOST_LINUX_SRC}"
  LD_EXTRA=(-lX11)
  if command -v aarch64-linux-gnu-gcc >/dev/null 2>&1; then
    CC_BIN="aarch64-linux-gnu-gcc"
  fi
elif [[ "${TARGET_PLATFORM}" == "linux" ]]; then
  UI_HOST_SRC="${NATIVE_UI_HOST_LINUX_SRC}"
  LD_EXTRA=(-lX11)
elif [[ "${TARGET_PLATFORM}" == "windows" ]]; then
  UI_HOST_SRC="${NATIVE_UI_HOST_WINDOWS_SRC}"
  LD_EXTRA=(-lgdi32 -luser32 -lole32 -lwindowscodecs -luuid)
  if [[ "${TARGET_ARCH}" == "x64" && "${HOST_ARCH}" != "x64" ]] && command -v x86_64-w64-mingw32-gcc >/dev/null 2>&1; then
    CC_BIN="x86_64-w64-mingw32-gcc"
  elif [[ "${TARGET_ARCH}" == "arm64" ]] && command -v aarch64-w64-mingw32-gcc >/dev/null 2>&1; then
    CC_BIN="aarch64-w64-mingw32-gcc"
  fi
fi

COMMON_SOURCES=(
  "${SOURCE_PATH}"
  "${UI_HOST_SRC}"
  "${NATIVE_SRC_DIR}/aivm_types.c"
  "${NATIVE_SRC_DIR}/aivm_vm.c"
  "${NATIVE_SRC_DIR}/aivm_program.c"
  "${NATIVE_SRC_DIR}/sys/aivm_syscall.c"
  "${NATIVE_SRC_DIR}/sys/aivm_syscall_contracts.c"
  "${NATIVE_SRC_DIR}/aivm_parity.c"
  "${NATIVE_SRC_DIR}/aivm_runtime.c"
  "${NATIVE_SRC_DIR}/aivm_c_api.c"
  "${NATIVE_SRC_DIR}/remote/aivm_remote_channel.c"
  "${NATIVE_SRC_DIR}/remote/aivm_remote_session.c"
)

"${CC_BIN}" -std=c17 -Wall -Wextra -Werror -O2 -DAIRUN_UI_HOST_EXTERNAL=1 "${CC_EXTRA[@]}" \
  -I "${NATIVE_INCLUDE}" \
  "${COMMON_SOURCES[@]}" \
  "${LD_EXTRA[@]}" \
  -o "${WRAPPER_PATH}"
chmod +x "${WRAPPER_PATH}"

"${CC_BIN}" -std=c17 -Wall -Wextra -Werror -O2 -DAIRUN_UI_HOST_EXTERNAL=1 -DAIRUN_MINIMAL_RUNTIME=1 "${CC_EXTRA[@]}" \
  -I "${NATIVE_INCLUDE}" \
  "${COMMON_SOURCES[@]}" \
  "${LD_EXTRA[@]}" \
  -o "${RUNTIME_PATH}"
chmod +x "${RUNTIME_PATH}"

if [[ "${TARGET_PLATFORM}" == "${HOST_PLATFORM}" && "${TARGET_ARCH}" == "${HOST_ARCH}" ]]; then
  cp "${WRAPPER_PATH}" "${HOST_WRAPPER_PATH}"
  chmod +x "${HOST_WRAPPER_PATH}"
  cp "${RUNTIME_PATH}" "${ROOT_DIR}/tools/${RUNTIME_BIN_NAME}"
  chmod +x "${ROOT_DIR}/tools/${RUNTIME_BIN_NAME}"
fi
