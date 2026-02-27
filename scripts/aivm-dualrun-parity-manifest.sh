#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP_DIR="${ROOT_DIR}/.tmp/aivm-dualrun-manifest"
MANIFEST="${1:-}"
REPORT="${2:-${TMP_DIR}/report.txt}"
PARITY_SHELL="${AIVM_PARITY_SHELL:-bash}"

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

if ! command -v "${PARITY_SHELL}" >/dev/null 2>&1; then
  echo "parity shell not found: ${PARITY_SHELL}" >&2
  exit 2
fi

case_count=0

while IFS='|' read -r name left_cmd right_cmd expected_status; do
  case_slug=""
  left_out=""
  right_out=""
  left_status=0
  right_status=0
  expected=0

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

  if [[ -n "${expected_status}" ]]; then
    if [[ ! "${expected_status}" =~ ^[0-9]+$ ]]; then
      echo "invalid expected status for case '${name}': ${expected_status}" >&2
      exit 2
    fi
    expected="${expected_status}"
  fi

  case_count=$((case_count + 1))
  case_slug="$(printf "%s" "${name}" | tr -c 'A-Za-z0-9._-' '_')"
  left_out="${TMP_DIR}/${case_slug}.left.out"
  right_out="${TMP_DIR}/${case_slug}.right.out"

  set +e
  "${PARITY_SHELL}" -lc "${left_cmd}" >"${left_out}" 2>&1
  left_status=$?
  "${PARITY_SHELL}" -lc "${right_cmd}" >"${right_out}" 2>&1
  right_status=$?
  set -e

  if [[ ${left_status} -ne ${expected} || ${right_status} -ne ${expected} ]]; then
    echo "case=${name}|status=status_mismatch|left_status=${left_status}|right_status=${right_status}|expected_status=${expected}|left_file=${left_out}|right_file=${right_out}" >> "${REPORT}"
    echo "status mismatch for case '${name}' (left=${left_status} right=${right_status} expected=${expected})" >&2
    exit 1
  fi

  if "${ROOT_DIR}/scripts/aivm-parity-compare.sh" "${left_out}" "${right_out}" >/dev/null; then
    echo "case=${name}|status=equal|left_status=${left_status}|right_status=${right_status}|left_file=${left_out}|right_file=${right_out}" >> "${REPORT}"
  else
    echo "case=${name}|status=diff|left_status=${left_status}|right_status=${right_status}|left_file=${left_out}|right_file=${right_out}" >> "${REPORT}"
    echo "parity mismatch for case '${name}'" >&2
    exit 1
  fi
done < "${MANIFEST}"

if [[ ${case_count} -eq 0 ]]; then
  echo "manifest contained no executable cases: ${MANIFEST}" >&2
  exit 2
fi

echo "parity manifest passed: ${case_count} case(s)"
