#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

TMP_DIR="${ROOT_DIR}/.tmp/airun-build-source-smoke"
APP_DIR="${TMP_DIR}/app"
FILE_BUILD_DIR="${TMP_DIR}/file-build"

to_base36() {
  local value="$1"
  local digits='0123456789abcdefghijklmnopqrstuvwxyz'
  local out=""
  local remainder
  if [[ "${value}" -eq 0 ]]; then
    printf '0'
    return 0
  fi
  while [[ "${value}" -gt 0 ]]; do
    remainder=$((value % 36))
    out="${digits:remainder:1}${out}"
    value=$((value / 36))
  done
  printf '%s' "${out}"
}

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

SCOPE_DIR="${TMP_DIR}/scope-reuse"
SCOPE_OUT_DIR="${TMP_DIR}/scope-reuse-out"
mkdir -p "${SCOPE_DIR}"
{
  echo 'Program#p1{'
  echo 'Export#e1(name=start)'
  echo 'Let#h1(name=helper){'
  echo 'Fn#hf1(params=a,b){'
  echo 'Block#hb1{'
  echo 'Return#hr1{Var#hv1(name=b)}'
  echo '}'
  echo '}'
  echo '}'
  echo 'Let#l1(name=start){'
  echo 'Fn#f1(params=args){'
  echo 'Block#b1{'
  block_i=0
  value_i=0
  while [[ $block_i -lt 91 ]]; do
    echo 'Block#b{'
    inner_i=0
    while [[ $inner_i -lt 45 ]]; do
      name="$(to_base36 "${value_i}")"
      printf 'Let#l(name=%s){Lit#v(value=0)}\n' "${name}"
      inner_i=$((inner_i + 1))
      value_i=$((value_i + 1))
    done
    echo '}'
    block_i=$((block_i + 1))
  done
  echo 'Return#r1{Call#c1(target=helper){Lit#ha(value=1)Lit#hb(value=2)}}'
  echo '}'
  echo '}'
  echo '}'
  echo '}'
} > "${SCOPE_DIR}/app.aos"

./tools/airun build "${SCOPE_DIR}/app.aos" --out "${SCOPE_OUT_DIR}" --no-cache >/dev/null
SCOPE_RUN_OUT="$(./tools/airun run "${SCOPE_OUT_DIR}/app.aibc1" 2>&1 || true)"
printf '%s\n' "${SCOPE_RUN_OUT}" | rg -q '^Ok#ok1\(type=int value=2\)$'
