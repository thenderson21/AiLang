#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="${ROOT_DIR}/.artifacts/airun-osx-arm64"

"${ROOT_DIR}/scripts/build-frontend.sh"

mkdir -p "${OUT_DIR}"

if [[ -x "${ROOT_DIR}/tools/airun" ]]; then
  cp "${ROOT_DIR}/tools/airun" "${OUT_DIR}/airun"
  chmod +x "${OUT_DIR}/airun"
  exit 0
fi

echo "native airun builder is not implemented yet; expected existing tools/airun" >&2
exit 1
