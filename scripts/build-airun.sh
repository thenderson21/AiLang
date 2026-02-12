#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="${ROOT_DIR}/.artifacts/airun-osx-arm64"

"${ROOT_DIR}/scripts/build-frontend.sh"

dotnet publish "${ROOT_DIR}/src/AiCLI/AiCLI.csproj" \
  -c Release \
  -r osx-arm64 \
  -p:PublishAot=true \
  --self-contained true \
  -o "${OUT_DIR}"

cp "${OUT_DIR}/airun" "${ROOT_DIR}/tools/airun"
chmod +x "${ROOT_DIR}/tools/airun"
