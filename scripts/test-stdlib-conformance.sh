#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MANIFEST_FILE="${ROOT_DIR}/Docs/Stdlib-Baseline-Manifest.tsv"

if [[ ! -f "${MANIFEST_FILE}" ]]; then
  echo "missing stdlib baseline manifest: ${MANIFEST_FILE}" >&2
  exit 1
fi

if ! command -v rg >/dev/null 2>&1; then
  echo "stdlib conformance requires rg" >&2
  exit 1
fi

manifest_module_count=0

while IFS=$'\t' read -r module_path exports_csv; do
  [[ -z "${module_path}" ]] && continue
  [[ "${module_path}" == \#* ]] && continue

  manifest_module_count=$((manifest_module_count + 1))
  module_file="${ROOT_DIR}/${module_path}"

  if [[ ! -f "${module_file}" ]]; then
    echo "stdlib baseline missing module: ${module_path}" >&2
    exit 1
  fi

  actual_exports="$(rg -o 'Export#[^\n]*name=([A-Za-z0-9_]+)' -r '$1' "${module_file}" | sort)"
  expected_exports="$(printf '%s\n' "${exports_csv}" | tr ',' '\n' | sort)"

  if [[ "${actual_exports}" != "${expected_exports}" ]]; then
    echo "stdlib baseline export drift: ${module_path}" >&2
    echo "expected:" >&2
    printf '%s\n' "${expected_exports}" >&2
    echo "actual:" >&2
    printf '%s\n' "${actual_exports}" >&2
    exit 1
  fi
done < "${MANIFEST_FILE}"

if [[ "${manifest_module_count}" -eq 0 ]]; then
  echo "stdlib baseline manifest is empty: ${MANIFEST_FILE}" >&2
  exit 1
fi

echo "stdlib conformance: PASS (${manifest_module_count} baseline modules)"
