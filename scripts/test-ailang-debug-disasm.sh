#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

TMP_DIR="${ROOT}/.tmp/test-ailang-debug-disasm"
rm -rf "${TMP_DIR}"
mkdir -p "${TMP_DIR}"

cat >"${TMP_DIR}/app.aos" <<'AOS'
Program#p1 {
  Export#e1(name=start)

  Let#l1(name=start) {
    Fn#f1 {
      Block#b1 {
        Return#r1 { Lit#n1(value=0) }
      }
    }
  }
}
AOS

./tools/ailang build "${TMP_DIR}/app.aos" --out "${TMP_DIR}" >/dev/null
OUT="$(./tools/ailang debug disasm "${TMP_DIR}/app.aibc1" 0 5)"
printf '%s\n' "$OUT" | grep -q $'^0\t'
printf '%s\n' "$OUT" | grep -q 'PUSH_INT'
