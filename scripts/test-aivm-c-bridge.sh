#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_DIR="${AIVM_C_BRIDGE_BUILD_DIR:-${ROOT_DIR}/.tmp/aivm-c-build-bridge}"
PARITY_REPORT="${AIVM_C_BRIDGE_PARITY_REPORT:-${ROOT_DIR}/.tmp/aivm-dualrun-manifest/report-bridge.txt}"

AIVM_BUILD_SHARED=1 \
  AIVM_C_BUILD_DIR="${BUILD_DIR}" \
  AIVM_PARITY_REPORT="${PARITY_REPORT}" \
  "${ROOT_DIR}/scripts/test-aivm-c.sh"

ctest --test-dir "${BUILD_DIR}" -R aivm_test_shared_bridge_loader --output-on-failure

echo "bridge smoke passed"
