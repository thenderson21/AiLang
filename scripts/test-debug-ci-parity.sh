#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

./scripts/bootstrap-golden-publish-fixtures.sh
./test.sh
./tools/ailang debug scenario examples/debug/scenarios/minimal.scenario.toml --name minimal
