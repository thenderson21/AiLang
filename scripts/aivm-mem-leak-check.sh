#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "usage: $0 <program-path-or-dir> [iterations]" >&2
  exit 2
fi

TARGET="$1"
ITERATIONS="${2:-20}"
REPORT="${AIVM_LEAK_REPORT:-/tmp/aivm-mem-leak-check.txt}"

if ! [[ "${ITERATIONS}" =~ ^[0-9]+$ ]] || [[ "${ITERATIONS}" -le 0 ]]; then
  echo "iterations must be a positive integer" >&2
  exit 2
fi

if ! command -v /usr/bin/time >/dev/null 2>&1; then
  echo "platform does not provide /usr/bin/time -l; leak check unavailable" >&2
  exit 3
fi

rss_values=()
for ((i=1; i<=ITERATIONS; i++)); do
  METRICS="/tmp/aivm-leak-metrics-${i}.txt"
  /usr/bin/time -l ./tools/airun run "${TARGET}" --vm=c >/dev/null 2>"${METRICS}" || true
  rss="$(rg -n "maximum resident set size" "${METRICS}" | awk '{print $1}' | head -n1 || true)"
  if [[ -z "${rss}" ]]; then
    rss=0
  fi
  rss_values+=("${rss}")
done

first="${rss_values[0]}"
last="${rss_values[$((ITERATIONS-1))]}"
growth=$((last - first))

{
  echo "AIVM_LEAK_CHECK"
  echo "target=${TARGET}"
  echo "iterations=${ITERATIONS}"
  echo "first_rss=${first}"
  echo "last_rss=${last}"
  echo "rss_growth=${growth}"
  echo "rss_series=$(IFS=,; echo "${rss_values[*]}")"
} > "${REPORT}"

echo "${REPORT}"

