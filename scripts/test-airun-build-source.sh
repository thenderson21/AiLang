#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

TMP_DIR="${ROOT_DIR}/.tmp/airun-build-source-smoke"
APP_DIR="${TMP_DIR}/app"
FILE_BUILD_DIR="${TMP_DIR}/file-build"

rm -rf "${TMP_DIR}"
mkdir -p "${TMP_DIR}"

./tools/airun init "${APP_DIR}"
./tools/airun build "${APP_DIR}/" --out "${APP_DIR}" --no-cache >/dev/null

cat > "${APP_DIR}/src/app.aos" <<'EOF'
Program#p1 {
  Export#e1(name=start)

  Let#l1(name=start) {
    Fn#f1(params=args) {
      Block#b1 {
        Call#c1(target=sys.stdout.writeLine) { Lit#s1(value="fresh-source-build") }
        Return#r1 { Lit#i1(value=0) }
      }
    }
  }
}
EOF

./tools/airun build "${APP_DIR}/src/app.aos" --out "${FILE_BUILD_DIR}" --no-cache >/dev/null

FILE_OUT="$(./tools/airun run "${FILE_BUILD_DIR}/app.aibc1" 2>&1)"
printf '%s\n' "${FILE_OUT}" | rg -q '^fresh-source-build$'

