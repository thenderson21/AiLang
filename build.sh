#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

print_usage() {
  cat <<'EOF'
Usage: ./build.sh [host|shared|wasm|all]

Builds AiLang tooling through the selected installed SDK.

Targets:
  host    Stage host tools from the selected installed SDK (default).
  shared  Delegated to AiVM; kept temporarily for migration compatibility.
  wasm    Delegated to AiVM; kept temporarily for migration compatibility.
  all     Stage host tools and run delegated compatibility targets.
EOF
}

run_target() {
  local target="$1"
  case "${target}" in
    host)
      "${ROOT_DIR}/scripts/build-ailang-native.sh"
      ;;
    shared)
      "${ROOT_DIR}/scripts/build-aivm-c-shared.sh"
      ;;
    wasm)
      "${ROOT_DIR}/scripts/build-aivm-wasm.sh"
      ;;
    all)
      run_target host
      run_target shared
      run_target wasm
      ;;
    -h|--help|help)
      print_usage
      ;;
    *)
      echo "unknown build target: ${target}" >&2
      print_usage >&2
      exit 1
      ;;
  esac
}

run_target "${1:-host}"
