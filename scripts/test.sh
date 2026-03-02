#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

AIRUN_BIN="${AIRUN_BIN:-./tools/airun}"

./scripts/bootstrap-golden-publish-fixtures.sh

"${AIRUN_BIN}" run --vm=ast src/compiler/aic.aos test examples/golden
