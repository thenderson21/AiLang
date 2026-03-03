#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BACKEND_PATH="${ROOT_DIR}/tools/airun-host"
WRAPPER_PATH="${ROOT_DIR}/tools/airun"
SOURCE_PATH="${ROOT_DIR}/src/AiCLI/native/airun.c"
UNAME_S="$(uname -s)"
UNAME_M="$(uname -m)"

case "${UNAME_S}" in
  Darwin) PLATFORM="osx" ;;
  Linux) PLATFORM="linux" ;;
  *)
    echo "build-airun.sh supports only macOS/Linux (got ${UNAME_S})" >&2
    exit 1
    ;;
esac

case "${UNAME_M}" in
  arm64|aarch64) ARCH="arm64" ;;
  x86_64|amd64) ARCH="x64" ;;
  *)
    echo "unsupported CPU architecture for airun wrapper build: ${UNAME_M}" >&2
    exit 1
    ;;
esac

OUT_DIR="${ROOT_DIR}/.artifacts/airun-${PLATFORM}-${ARCH}"

"${ROOT_DIR}/scripts/build-frontend.sh"

mkdir -p "${OUT_DIR}"

if [[ ! -x "${BACKEND_PATH}" ]]; then
  if [[ -x "${WRAPPER_PATH}" ]]; then
    cp "${WRAPPER_PATH}" "${BACKEND_PATH}"
    chmod +x "${BACKEND_PATH}"
  else
    echo "missing backend host binary; expected ${BACKEND_PATH} or ${WRAPPER_PATH}" >&2
    exit 1
  fi
fi

cc -std=c17 -Wall -Wextra -Werror -O2 "${SOURCE_PATH}" -o "${WRAPPER_PATH}"
chmod +x "${WRAPPER_PATH}"
cp "${WRAPPER_PATH}" "${OUT_DIR}/airun"
chmod +x "${OUT_DIR}/airun"
