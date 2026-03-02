#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

C_VM_BIN="${C_VM_BIN:-./tools/airun}"
C_VM_RUN_ARGS="${C_VM_RUN_ARGS:-run}"
REF_BIN="${REF_BIN:-./tools/airun}"
REF_RUN_ARGS="${REF_RUN_ARGS:-run}"
CASE_FILE="${CASE_FILE:-}"

if [[ -n "${CASE_FILE}" && ! -f "${CASE_FILE}" ]]; then
  echo "CASE_FILE does not exist: ${CASE_FILE}" >&2
  exit 1
fi

TMP_CASES=""
if [[ -z "${CASE_FILE}" ]]; then
  TMP_CASES="$(mktemp -t c-vm-cases-XXXXXX.txt)"
  CASE_FILE="${TMP_CASES}"
  cat > "${CASE_FILE}" <<'EOF'
# program|args
examples/hello.aos|
examples/echo.aos|hello
examples/args.aos|alpha beta gamma
examples/bench/loop_compute.aos|
examples/bench/str_concat.aos|
examples/bench/map_create.aos|
EOF
fi

cleanup() {
  rm -f "${TMP_CASES}"
}
trap cleanup EXIT

run_one() {
  local bin="$1"
  local run_args_str="$2"
  local program="$3"
  local args_str="$4"
  local stdout_file="$5"
  local stderr_file="$6"
  local code_file="$7"
  local run_args_arr=()
  local case_args=()

  if [[ -n "${run_args_str}" ]]; then
    IFS=' ' read -r -a run_args_arr <<< "${run_args_str}"
  fi

  if [[ -n "${args_str}" ]]; then
    IFS=' ' read -r -a case_args <<< "${args_str}"
  fi

  set +e
  if [[ -n "${args_str}" ]]; then
    "${bin}" "${run_args_arr[@]}" "${program}" "${case_args[@]}" >"${stdout_file}" 2>"${stderr_file}"
  else
    "${bin}" "${run_args_arr[@]}" "${program}" >"${stdout_file}" 2>"${stderr_file}"
  fi
  local code=$?
  set -e
  echo "${code}" > "${code_file}"
}

passes=0
failures=0

while IFS='|' read -r program args_str; do
  if [[ -z "${program}" || "${program}" =~ ^# ]]; then
    continue
  fi

  if [[ ! -f "${program}" ]]; then
    echo "FAIL ${program}: program file not found" >&2
    failures=$((failures + 1))
    continue
  fi

  REF_OUT="$(mktemp -t c-vm-ref-out-XXXXXX.txt)"
  REF_ERR="$(mktemp -t c-vm-ref-err-XXXXXX.txt)"
  REF_CODE="$(mktemp -t c-vm-ref-code-XXXXXX.txt)"
  VM_OUT="$(mktemp -t c-vm-vm-out-XXXXXX.txt)"
  VM_ERR="$(mktemp -t c-vm-vm-err-XXXXXX.txt)"
  VM_CODE="$(mktemp -t c-vm-vm-code-XXXXXX.txt)"

  run_one "${REF_BIN}" "${REF_RUN_ARGS}" "${program}" "${args_str}" "${REF_OUT}" "${REF_ERR}" "${REF_CODE}"
  run_one "${C_VM_BIN}" "${C_VM_RUN_ARGS}" "${program}" "${args_str}" "${VM_OUT}" "${VM_ERR}" "${VM_CODE}"

  ref_code="$(cat "${REF_CODE}")"
  vm_code="$(cat "${VM_CODE}")"

  case_failed=0

  if [[ "${ref_code}" != "${vm_code}" ]]; then
    echo "FAIL ${program}: exit code mismatch ref=${ref_code} vm=${vm_code}" >&2
    case_failed=1
  fi

  if ! cmp -s "${REF_OUT}" "${VM_OUT}"; then
    echo "FAIL ${program}: stdout mismatch" >&2
    diff -u "${REF_OUT}" "${VM_OUT}" || true
    case_failed=1
  fi

  if ! cmp -s "${REF_ERR}" "${VM_ERR}"; then
    echo "FAIL ${program}: stderr mismatch" >&2
    diff -u "${REF_ERR}" "${VM_ERR}" || true
    case_failed=1
  fi

  if [[ "${case_failed}" -eq 1 ]]; then
    failures=$((failures + 1))
  else
    echo "PASS ${program}"
    passes=$((passes + 1))
  fi

  rm -f "${REF_OUT}" "${REF_ERR}" "${REF_CODE}" "${VM_OUT}" "${VM_ERR}" "${VM_CODE}"
done < "${CASE_FILE}"

echo "summary pass=${passes} fail=${failures}"
if [[ "${failures}" -ne 0 ]]; then
  exit 1
fi
