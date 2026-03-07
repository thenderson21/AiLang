#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MANIFEST_FILE="${ROOT_DIR}/Docs/SDK-Docs-Manifest.tsv"

if [[ $# -ne 1 ]]; then
  echo "usage: $0 <dest-root>" >&2
  exit 1
fi

DEST_ROOT="$1"

if [[ ! -f "${MANIFEST_FILE}" ]]; then
  echo "missing sdk docs manifest: ${MANIFEST_FILE}" >&2
  exit 1
fi

staged_count=0

while IFS=$'\t' read -r source_path target_path; do
  [[ -z "${source_path}" ]] && continue
  [[ "${source_path}" == \#* ]] && continue

  source_file="${ROOT_DIR}/${source_path}"
  target_file="${DEST_ROOT}/${target_path}"

  if [[ ! -f "${source_file}" ]]; then
    echo "sdk docs manifest missing file: ${source_path}" >&2
    exit 1
  fi

  mkdir -p "$(dirname "${target_file}")"
  cp "${source_file}" "${target_file}"
  staged_count=$((staged_count + 1))
done < "${MANIFEST_FILE}"

if [[ "${staged_count}" -eq 0 ]]; then
  echo "sdk docs manifest is empty: ${MANIFEST_FILE}" >&2
  exit 1
fi

echo "sdk docs staged: ${staged_count} files -> ${DEST_ROOT}"
