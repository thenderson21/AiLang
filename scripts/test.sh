#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

./scripts/bootstrap-golden-publish-fixtures.sh

./tools/airun run --vm=ast src/compiler/aic.aos test examples/golden
