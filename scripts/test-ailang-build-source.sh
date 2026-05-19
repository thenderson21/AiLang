#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

TMP_DIR="${ROOT_DIR}/.tmp/ailang-build-source-smoke"
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

./tools/ailang init "${APP_DIR}"
./tools/ailang build "${APP_DIR}/" --out "${APP_DIR}" --no-cache >/dev/null

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

./tools/ailang build "${APP_DIR}/src/app.aos" --out "${FILE_BUILD_DIR}" --no-cache >/dev/null

FILE_OUT="$(./tools/ailang run "${FILE_BUILD_DIR}/app.aibc1" 2>&1)"
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

./tools/ailang build "${SCOPE_DIR}/app.aos" --out "${SCOPE_OUT_DIR}" --no-cache >/dev/null
SCOPE_RUN_OUT="$(./tools/ailang run "${SCOPE_OUT_DIR}/app.aibc1" 2>&1 || true)"
printf '%s\n' "${SCOPE_RUN_OUT}" | rg -q '^Ok#ok1\(type=int value=2\)$'

RECURSE_DIR="${TMP_DIR}/recursive-locals"
RECURSE_OUT_DIR="${TMP_DIR}/recursive-locals-out"
mkdir -p "${RECURSE_DIR}"
cat > "${RECURSE_DIR}/app.aos" <<'EOF'
Program#rp1 {
  Export#re1(name=start)

  Let#rl1(name=recurse) {
    Fn#rf1(params=n,seed) {
      Block#rb1 {
        Let#ra1(name=a) { Add#radd1 { Var#rv1(name=seed) Lit#ri1(value=1) } }
        Let#ra2(name=b) { Add#radd2 { Var#rv2(name=a) Lit#ri2(value=1) } }
        Let#ra3(name=c) { Add#radd3 { Var#rv3(name=b) Lit#ri3(value=1) } }
        Let#ra4(name=d) { Add#radd4 { Var#rv4(name=c) Lit#ri4(value=1) } }
        Let#ra5(name=e) { Add#radd5 { Var#rv5(name=d) Lit#ri5(value=1) } }
        Let#ra6(name=f) { Add#radd6 { Var#rv6(name=e) Lit#ri6(value=1) } }
        Let#ra7(name=g) { Add#radd7 { Var#rv7(name=f) Lit#ri7(value=1) } }
        Let#ra8(name=h) { Add#radd8 { Var#rv8(name=g) Lit#ri8(value=1) } }
        If#rif1 {
          Eq#req1 { Var#rv9(name=n) Lit#ri9(value=0) }
          Block#rb2 { Return#rr1 { Var#rv10(name=h) } }
          Block#rb3 {
            Return#rr2 {
              Call#rc1(target=recurse) {
                Add#radd9 { Var#rv11(name=n) Lit#ri10(value=-1) }
                Var#rv12(name=h)
              }
            }
          }
        }
      }
    }
  }

  Let#rl2(name=start) {
    Fn#rf2(params=args) {
      Block#rb4 {
        Return#rr3 { Call#rc2(target=recurse) { Lit#ri11(value=450) Lit#ri12(value=0) } }
      }
    }
  }
}
EOF

./tools/ailang build "${RECURSE_DIR}/app.aos" --out "${RECURSE_OUT_DIR}" --no-cache >/dev/null
RECURSE_RUN_OUT="$(./tools/ailang run "${RECURSE_OUT_DIR}/app.aibc1" 2>&1 || true)"
printf '%s\n' "${RECURSE_RUN_OUT}" | rg -q '^Ok#ok1\(type=int value=3608\)$'

UI_TEXT_DIR="${TMP_DIR}/ui-text-events"
UI_TEXT_OUT_DIR="${TMP_DIR}/ui-text-events-out"
mkdir -p "${UI_TEXT_DIR}"
cat > "${UI_TEXT_DIR}/app.aos" <<'EOF'
Program#p1 {
  Export#e1(name=start)

  Let#l1(name=start) {
    Fn#f1(params=args) {
      Block#b1 {
        Let#l2(name=window) {
          Call#c1(target=sys.ui.createWindow) {
            Lit#s1(value="TextProbe")
            Lit#i1(value=320)
            Lit#i2(value=120)
          }
        }
        Call#c2(target=sys.ui.pollEvent) { Var#v1(name=window) }
        Call#c3(target=sys.ui.pollEvent) { Var#v2(name=window) }
        Return#r1 { Lit#i3(value=0) }
      }
    }
  }
}
EOF

UI_TEXT_CAPTURE_DIR="${TMP_DIR}/ui-text-capture"
./tools/ailang debug capture run "${UI_TEXT_DIR}/app.aos" --out "${UI_TEXT_CAPTURE_DIR}" --inject-text "X" --inject-close --no-cache >/dev/null
grep -q 'type = "text"' "${UI_TEXT_CAPTURE_DIR}/events.toml"
grep -q 'text = "X"' "${UI_TEXT_CAPTURE_DIR}/events.toml"
