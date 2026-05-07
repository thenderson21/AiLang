#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
echo "build-airun.sh is deprecated; AiLang no longer builds C launchers locally." >&2
exec "${ROOT_DIR}/scripts/stage-installed-toolchain.sh"
