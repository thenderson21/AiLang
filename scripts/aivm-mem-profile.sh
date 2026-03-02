#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "usage: $0 <program-path-or-dir> [--vm=c|--vm=bytecode|--vm=ast]" >&2
  exit 2
fi

TARGET="$1"
shift || true
VM_MODE="${1:---vm=c}"
REPORT="${AIVM_MEM_REPORT:-/tmp/aivm-mem-profile.txt}"

if command -v /usr/bin/time >/dev/null 2>&1; then
  /usr/bin/time -l ./tools/airun run "${TARGET}" "${VM_MODE}" >"${REPORT}.stdout" 2>"${REPORT}.metrics" || true
  {
    echo "AIVM_MEM_PROFILE"
    echo "target=${TARGET}"
    echo "vm_mode=${VM_MODE}"
    echo "---metrics---"
    cat "${REPORT}.metrics"
    echo "---stdout---"
    sed -n '1,120p' "${REPORT}.stdout"
  } > "${REPORT}"
  echo "${REPORT}"
  exit 0
fi

echo "platform does not provide /usr/bin/time -l; memory profile unavailable" >&2
exit 3

