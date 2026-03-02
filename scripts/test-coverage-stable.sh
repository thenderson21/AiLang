#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

RESULTS_DIR="${1:-/tmp/ailang-coverage-stable}"
mkdir -p "${RESULTS_DIR}"

./scripts/test.sh

REPORT="${RESULTS_DIR}/coverage-placeholder.txt"
{
  echo "coverage collection is pending native harness migration"
  echo "validation suite status: pass"
} > "${REPORT}"

echo "coverage report: ${REPORT}"
sed -n '1,3p' "${REPORT}"
