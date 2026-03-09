#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

./scripts/test-stdlib-conformance.sh
./scripts/test-stdlib-capabilities.sh
./scripts/test-airun-init.sh
./scripts/test-airun-build-source.sh

# Samples are language-level showcases: direct syscall targets are forbidden.
if command -v rg >/dev/null 2>&1; then
  if rg -n --no-heading 'target=sys[._]' samples -g '*.aos'; then
    echo "sample policy violation: direct sys.* targets are not allowed under samples/" >&2
    exit 1
  fi
else
  if grep -REn 'target=sys[._]' samples --include='*.aos'; then
    echo "sample policy violation: direct sys.* targets are not allowed under samples/" >&2
    exit 1
  fi
fi

./scripts/test-aivm-c.sh
if command -v emcc >/dev/null 2>&1 && command -v wasmtime >/dev/null 2>&1; then
  ./scripts/test-wasm-golden.sh
else
  echo "skipping wasm golden tests: emcc and/or wasmtime not found"
fi
AIVM_DOD_RUN_TESTS=0 AIVM_DOD_RUN_BENCH=0 ./scripts/aivm-parity-dashboard.sh "${ROOT_DIR}/.tmp/aivm-parity-dashboard-ci.md"
