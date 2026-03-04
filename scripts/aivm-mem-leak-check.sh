#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "usage: $0 <program-path-or-dir> [iterations]" >&2
  exit 2
fi

TARGET="$1"
ITERATIONS="${2:-20}"
REPORT="${AIVM_LEAK_REPORT:-/tmp/aivm-mem-leak-check.txt}"
# Back-compat: accept either variable name; explicit *_GROWTH takes precedence.
if [[ -n "${AIVM_LEAK_MAX_GROWTH_KB:-}" ]]; then
  MAX_GROWTH_KB="${AIVM_LEAK_MAX_GROWTH_KB}"
else
  MAX_GROWTH_KB="${AIVM_LEAK_MAX_RSS_GROWTH_KB:-1024}"
fi

if ! [[ "${ITERATIONS}" =~ ^[0-9]+$ ]] || [[ "${ITERATIONS}" -le 0 ]]; then
  echo "iterations must be a positive integer" >&2
  exit 2
fi

if ! [[ "${MAX_GROWTH_KB}" =~ ^[0-9]+$ ]]; then
  echo "AIVM_LEAK_MAX_GROWTH_KB must be a non-negative integer" >&2
  exit 2
fi

if ! command -v /usr/bin/time >/dev/null 2>&1; then
  echo "platform does not provide /usr/bin/time; leak check unavailable" >&2
  exit 3
fi

platform="$(uname -s)"
time_mode=""
rss_pattern=""
case "${platform}" in
  Darwin)
    time_mode="-l"
    rss_pattern="maximum resident set size"
    ;;
  Linux)
    time_mode="-v"
    rss_pattern="Maximum resident set size (kbytes):"
    ;;
  *)
    echo "unsupported platform for leak check: ${platform}" >&2
    exit 3
    ;;
esac

rss_values=()
for ((i=1; i<=ITERATIONS; i++)); do
  METRICS="/tmp/aivm-leak-metrics-$$-${i}.txt"
  set +e
  /usr/bin/time ${time_mode} ./tools/airun run "${TARGET}" --vm=c >/dev/null 2>"${METRICS}"
  run_rc=$?
  set -e

  if [[ "${platform}" == "Darwin" ]]; then
    rss="$(grep -F "${rss_pattern}" "${METRICS}" | awk '{print $1}' | head -n1 || true)"
  else
    rss="$(grep -F "${rss_pattern}" "${METRICS}" | awk '{print $NF}' | head -n1 || true)"
  fi

  if [[ -z "${rss}" ]]; then
    echo "leak check unavailable: failed to parse RSS at iteration ${i}" >&2
    if [[ ${run_rc} -ne 0 ]]; then
      echo "command exit=${run_rc}" >&2
    fi
    exit 3
  fi

  # Some hosts return non-zero from /usr/bin/time despite recording metrics.
  if [[ ${run_rc} -ne 0 ]]; then
    echo "warning: /usr/bin/time returned ${run_rc} at iteration ${i}; using recorded RSS metric" >&2
  fi

  rss_values+=("${rss}")
done

first="${rss_values[0]}"
last="${rss_values[$((ITERATIONS-1))]}"
growth=$((last - first))

status="PASS"
if [[ "${growth}" -gt "${MAX_GROWTH_KB}" ]]; then
  status="FAIL"
fi

{
  echo "AIVM_LEAK_CHECK"
  echo "target=${TARGET}"
  echo "platform=${platform}"
  echo "time_mode=${time_mode}"
  echo "iterations=${ITERATIONS}"
  echo "max_growth_kb_threshold=${MAX_GROWTH_KB}"
  echo "first_rss=${first}"
  echo "last_rss=${last}"
  echo "rss_growth=${growth}"
  echo "status=${status}"
  echo "rss_series=$(IFS=,; echo "${rss_values[*]}")"
} > "${REPORT}"

echo "${REPORT}"

if [[ "${status}" == "FAIL" ]]; then
  echo "leak check failed: rss growth ${growth}KB exceeded threshold ${MAX_GROWTH_KB}KB" >&2
  exit 1
fi
