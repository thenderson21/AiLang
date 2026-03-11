#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

TMP_DIR="${ROOT_DIR}/.tmp/test-airun-traced-syscalls"
rm -rf "${TMP_DIR}"
mkdir -p "${TMP_DIR}"

cat > "${TMP_DIR}/app.aos" <<'EOF'
Program#p1 {
  Export#e1(name=start)

  Let#l1(name=start) {
    Fn#f1(params=args) {
      Block#b1 {
        Let#l2(name=bytes) {
          Call#c1(target=sys.bytes.fromUtf8String) {
            Lit#s1(value="hello")
          }
        }
        Call#c2(target=sys.stdout.writeLine) {
          Call#c3(target=sys.bytes.toBase64) {
            Var#v1(name=bytes)
          }
        }
        Return#r1 { Lit#n1(value=0) }
      }
    }
  }
}
EOF

OUTPUT="$("${ROOT_DIR}/tools/airun" debug trace run "${TMP_DIR}/app.aos" --no-cache 2>&1)"
printf '%s\n' "${OUTPUT}"

if ! printf '%s\n' "${OUTPUT}" | grep -q '^aGVsbG8=$'; then
  echo "traced airun regression: sys.bytes.fromUtf8String did not round-trip under debug trace" >&2
  exit 1
fi
