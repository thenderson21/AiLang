#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

RESULTS_DIR="${1:-/tmp/ailang-coverage-stable}"
FILTER='FullyQualifiedName!~Serve_&FullyQualifiedName!~Bench_'

rm -rf "${RESULTS_DIR}"

dotnet test tests/AiLang.Tests/AiLang.Tests.csproj \
  -c Release \
  --no-restore \
  --collect:"XPlat Code Coverage" \
  --results-directory "${RESULTS_DIR}" \
  --filter "${FILTER}"

COVERAGE_FILE="$(find "${RESULTS_DIR}" -name coverage.cobertura.xml | head -n1 || true)"
if [[ -z "${COVERAGE_FILE}" ]]; then
  echo "coverage report not found in ${RESULTS_DIR}" >&2
  exit 1
fi

echo "coverage report: ${COVERAGE_FILE}"
sed -n '1,3p' "${COVERAGE_FILE}"
