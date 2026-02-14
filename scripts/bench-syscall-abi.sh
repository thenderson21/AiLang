#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BASE_REF="${1:-main}"
CANDIDATE_REF="${2:-HEAD}"
RUNS="${RUNS:-40}"

if ! [[ "${RUNS}" =~ ^[0-9]+$ ]] || [[ "${RUNS}" -lt 1 ]]; then
  echo "RUNS must be a positive integer, got: ${RUNS}" >&2
  exit 1
fi

WORK_BASE="$(mktemp -d -t abi-base-XXXXXX)"
WORK_CANDIDATE="$(mktemp -d -t abi-candidate-XXXXXX)"
HOT_AOS="$(mktemp -t abi-hot-XXXXXX.aos)"
REPORT_BASE="$(mktemp -t abi-base-report-XXXXXX.txt)"
REPORT_CANDIDATE="$(mktemp -t abi-candidate-report-XXXXXX.txt)"

cleanup() {
  rm -f "${HOT_AOS}" "${REPORT_BASE}" "${REPORT_CANDIDATE}"
  git -C "${ROOT_DIR}" worktree remove --force "${WORK_BASE}" >/dev/null 2>&1 || true
  git -C "${ROOT_DIR}" worktree remove --force "${WORK_CANDIDATE}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

cat > "${HOT_AOS}" <<'EOF'
Program#p1 {
  Let#l1(name=hot) {
    Fn#f1(params=n) {
      Block#b1 {
        If#if1 {
          Eq#e1 { Var#v1(name=n) Lit#i0(value=0) }
          Block#b2 { Return#r1 { Lit#i1(value=0) } }
          Block#b3 {
            Return#r2 {
              Add#a1 {
                Call#c1(target=sys.str_utf8ByteCount) { Lit#s1(value="abcdefgh") }
                Call#c2(target=hot) { Add#a2 { Var#v2(name=n) Lit#i2(value=-1) } }
              }
            }
          }
        }
      }
    }
  }
  Call#c3(target=hot) { Lit#i3(value=2000) }
}
EOF

git -C "${ROOT_DIR}" worktree add --detach "${WORK_BASE}" "${BASE_REF}" >/dev/null
git -C "${ROOT_DIR}" worktree add --detach "${WORK_CANDIDATE}" "${CANDIDATE_REF}" >/dev/null

bench_ref() {
  local ref_name="$1"
  local worktree="$2"
  local report_file="$3"
  local binary_path="${worktree}/tools/airun"
  local build_log
  build_log="$(mktemp -t abi-build-${ref_name//\//_}-XXXXXX.log)"

  if ! (
    cd "${worktree}"
    ./scripts/build-airun.sh >"${build_log}" 2>&1
  ); then
    echo "build failed for ref ${ref_name}" >&2
    tail -n 80 "${build_log}" >&2
    rm -f "${build_log}"
    exit 1
  fi
  rm -f "${build_log}"

  python3 - "$binary_path" "${HOT_AOS}" "${RUNS}" > "${report_file}" <<'PY'
import statistics
import subprocess
import sys
import time

binary = sys.argv[1]
program = sys.argv[2]
runs = int(sys.argv[3])
times = []
for i in range(runs):
    t0 = time.perf_counter()
    proc = subprocess.run([binary, "run", program], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if proc.returncode != 0:
        raise SystemExit(f"benchmark run failed with exit code {proc.returncode} at iteration {i+1}")
    times.append((time.perf_counter() - t0) * 1000.0)

times.sort()
print(f"avg_ms={statistics.mean(times):.3f}")
print(f"median_ms={statistics.median(times):.3f}")
print(f"p90_ms={times[int(len(times) * 0.9) - 1]:.3f}")
print(f"min_ms={times[0]:.3f}")
print(f"max_ms={times[-1]:.3f}")
PY

  local size_bytes
  size_bytes="$(stat -f '%z' "${binary_path}")"
  echo "size_bytes=${size_bytes}" >> "${report_file}"
}

bench_ref "${BASE_REF}" "${WORK_BASE}" "${REPORT_BASE}"
bench_ref "${CANDIDATE_REF}" "${WORK_CANDIDATE}" "${REPORT_CANDIDATE}"

get_metric() {
  local file="$1"
  local key="$2"
  grep "^${key}=" "${file}" | head -n1 | cut -d= -f2
}

base_avg="$(get_metric "${REPORT_BASE}" "avg_ms")"
base_p90="$(get_metric "${REPORT_BASE}" "p90_ms")"
base_size="$(get_metric "${REPORT_BASE}" "size_bytes")"
candidate_avg="$(get_metric "${REPORT_CANDIDATE}" "avg_ms")"
candidate_p90="$(get_metric "${REPORT_CANDIDATE}" "p90_ms")"
candidate_size="$(get_metric "${REPORT_CANDIDATE}" "size_bytes")"

python3 - "${base_avg}" "${candidate_avg}" "${base_size}" "${candidate_size}" "${base_p90}" "${candidate_p90}" <<'PY'
import sys

base_avg = float(sys.argv[1])
candidate_avg = float(sys.argv[2])
base_size = int(sys.argv[3])
candidate_size = int(sys.argv[4])
base_p90 = float(sys.argv[5])
candidate_p90 = float(sys.argv[6])

def pct_change(new, old):
    if old == 0:
        return 0.0
    return ((new - old) / old) * 100.0

print(f"base_avg_ms={base_avg:.3f}")
print(f"candidate_avg_ms={candidate_avg:.3f}")
print(f"avg_delta_pct={pct_change(candidate_avg, base_avg):+.3f}")
print(f"base_p90_ms={base_p90:.3f}")
print(f"candidate_p90_ms={candidate_p90:.3f}")
print(f"p90_delta_pct={pct_change(candidate_p90, base_p90):+.3f}")
print(f"base_size_bytes={base_size}")
print(f"candidate_size_bytes={candidate_size}")
print(f"size_delta_bytes={candidate_size - base_size:+d}")
PY
