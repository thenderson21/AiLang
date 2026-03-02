#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

PROGRAM="${1:-examples/bench/loop_compute.aos}"
shift || true

C_VM_BIN="${C_VM_BIN:-./tools/airun}"
C_VM_RUN_ARGS="${C_VM_RUN_ARGS:-run}"
PROGRAM_ARGS="${PROGRAM_ARGS:-}"
PROFILE_MODE="${PROFILE_MODE:-auto}"
PERF_RUNS="${PERF_RUNS:-5}"

if [[ ! -f "${PROGRAM}" ]]; then
  echo "program does not exist: ${PROGRAM}" >&2
  exit 1
fi

if ! [[ "${PERF_RUNS}" =~ ^[0-9]+$ ]] || [[ "${PERF_RUNS}" -lt 1 ]]; then
  echo "PERF_RUNS must be a positive integer, got: ${PERF_RUNS}" >&2
  exit 1
fi

IFS=' ' read -r -a C_VM_RUN_ARR <<< "${C_VM_RUN_ARGS}"
CMD=( "${C_VM_BIN}" "${C_VM_RUN_ARR[@]}" "${PROGRAM}" )
if [[ -n "${PROGRAM_ARGS}" ]]; then
  IFS=' ' read -r -a PROG_ARGS_ARR <<< "${PROGRAM_ARGS}"
  CMD+=( "${PROG_ARGS_ARR[@]}" )
fi
if [[ "$#" -gt 0 ]]; then
  CMD+=( "$@" )
fi

mkdir -p .artifacts/profile/c-vm
STAMP="$(date '+%Y%m%d-%H%M%S')"
OUT_DIR=".artifacts/profile/c-vm/${STAMP}"
mkdir -p "${OUT_DIR}"

STDOUT_PATH="${OUT_DIR}/stdout.txt"
PROFILE_PATH="${OUT_DIR}/profile.txt"

if [[ "${PROFILE_MODE}" == "auto" ]]; then
  if command -v perf >/dev/null 2>&1 && perf stat -d -r 1 -- true >/dev/null 2>&1; then
    PROFILE_MODE="perf"
  else
    PROFILE_MODE="time"
  fi
fi

echo "profile_mode=${PROFILE_MODE}"
echo "program=${PROGRAM}"
echo "output_dir=${OUT_DIR}"

if [[ "${PROFILE_MODE}" == "perf" ]]; then
  perf stat -d -r "${PERF_RUNS}" -- "${CMD[@]}" >"${STDOUT_PATH}" 2>"${PROFILE_PATH}"
elif [[ "${PROFILE_MODE}" == "time" ]]; then
  if [[ "$(uname -s)" == "Darwin" ]]; then
    /usr/bin/time -p "${CMD[@]}" >"${STDOUT_PATH}" 2>"${PROFILE_PATH}"
  else
    /usr/bin/time -v "${CMD[@]}" >"${STDOUT_PATH}" 2>"${PROFILE_PATH}"
  fi
else
  echo "Unsupported PROFILE_MODE: ${PROFILE_MODE}. Use auto, perf, or time." >&2
  exit 1
fi

echo "stdout=${STDOUT_PATH}"
echo "profile=${PROFILE_PATH}"
