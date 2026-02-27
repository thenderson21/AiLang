#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP_DIR="${ROOT_DIR}/.tmp/aivm-dualrun-manifest"
MANIFEST="${1:-}"
REPORT="${2:-${TMP_DIR}/report.txt}"

if [[ -z "${MANIFEST}" ]]; then
  echo "usage: $0 <manifest-file> [report-file]" >&2
  exit 2
fi

if [[ ! -f "${MANIFEST}" ]]; then
  echo "missing manifest file: ${MANIFEST}" >&2
  exit 2
fi

mkdir -p "${TMP_DIR}"
: > "${REPORT}"

case_count=0

while IFS='|' read -r name left_cmd right_cmd; do
  case_slug=""
  left_out=""
  right_out=""

  if [[ -z "${name}" ]]; then
    continue
  fi
  if [[ "${name}" == \#* ]]; then
    continue
  fi

  if [[ -z "${left_cmd}" || -z "${right_cmd}" ]]; then
    echo "invalid manifest row for case '${name}'" >&2
    exit 2
  fi

  case_count=$((case_count + 1))
  case_slug="$(printf "%s" "${name}" | tr -c 'A-Za-z0-9._-' '_')"
  left_out="${TMP_DIR}/${case_slug}.left.out"
  right_out="${TMP_DIR}/${case_slug}.right.out"

  /bin/zsh -lc "${left_cmd}" >"${left_out}" 2>&1
  /bin/zsh -lc "${right_cmd}" >"${right_out}" 2>&1

  if "${ROOT_DIR}/scripts/aivm-parity-compare.sh" "${left_out}" "${right_out}" >/dev/null; then
    echo "case=${name}|status=equal|left_file=${left_out}|right_file=${right_out}" >> "${REPORT}"
  else
    echo "case=${name}|status=diff|left_file=${left_out}|right_file=${right_out}" >> "${REPORT}"
    echo "parity mismatch for case '${name}'" >&2
    exit 1
  fi
done < "${MANIFEST}"

if [[ ${case_count} -eq 0 ]]; then
  echo "manifest contained no executable cases: ${MANIFEST}" >&2
  exit 2
fi

echo "parity manifest passed: ${case_count} case(s)"
