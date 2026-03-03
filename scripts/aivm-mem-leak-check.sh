#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "usage: $0 <program-path-or-dir> [iterations]" >&2
  exit 2
fi

TARGET="$1"
ITERATIONS="${2:-20}"
REPORT="${AIVM_LEAK_REPORT:-/tmp/aivm-mem-leak-check.txt}"
MAX_GROWTH_KB="${AIVM_LEAK_MAX_RSS_GROWTH_KB:-1024}"

if ! [[ "${ITERATIONS}" =~ ^[0-9]+$ ]] || [[ "${ITERATIONS}" -le 0 ]]; then
  echo "iterations must be a positive integer" >&2
  exit 2
fi

if ! [[ "${MAX_GROWTH_KB}" =~ ^-?[0-9]+$ ]]; then
  echo "AIVM_LEAK_MAX_RSS_GROWTH_KB must be an integer" >&2
  exit 2
fi

if ! command -v /usr/bin/time >/dev/null 2>&1; then
  echo "platform does not provide /usr/bin/time; leak check unavailable" >&2
  exit 3
fi

TIME_ARGS=(-v)
if [[ "$(uname -s)" == "Darwin" ]]; then
  TIME_ARGS=(-l)
fi

parse_rss_kb() {
  local metrics_path="$1"
  local rss
  rss="$(sed -nE 's/^[[:space:]]*([0-9]+)[[:space:]]+maximum resident set size$/\1/p' "${metrics_path}" | head -n1 || true)"
  if [[ -z "${rss}" ]]; then
    rss="$(sed -nE 's/^.*Maximum resident set size \(kbytes\):[[:space:]]*([0-9]+).*$/\1/p' "${metrics_path}" | head -n1 || true)"
  fi
  echo "${rss}"
}

rss_values=()
for ((i=1; i<=ITERATIONS; i++)); do
  METRICS="/tmp/aivm-leak-metrics-${i}.txt"
  set +e
  /usr/bin/time "${TIME_ARGS[@]}" ./tools/airun run "${TARGET}" --vm=c >/dev/null 2>"${METRICS}"
  run_rc=$?
  set -e
  rss="$(parse_rss_kb "${METRICS}")"
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
gate_status="PASS"
if (( growth > MAX_GROWTH_KB )); then
  gate_status="FAIL"
fi

{
  echo "AIVM_LEAK_CHECK"
  echo "target=${TARGET}"
  echo "iterations=${ITERATIONS}"
  echo "max_growth_kb=${MAX_GROWTH_KB}"
  echo "first_rss=${first}"
  echo "last_rss=${last}"
  echo "rss_growth=${growth}"
  echo "gate_status=${gate_status}"
  echo "rss_series=$(IFS=,; echo "${rss_values[*]}")"
} > "${REPORT}"

echo "${REPORT}"

if [[ "${gate_status}" != "PASS" ]]; then
  echo "leak gate failed: rss growth ${growth} KB exceeded ${MAX_GROWTH_KB} KB" >&2
  exit 1
fi
