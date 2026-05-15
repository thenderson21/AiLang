#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

TMP_DIR="${ROOT_DIR}/.tmp/parser-selfhost"
rm -rf "${TMP_DIR}"
mkdir -p "${TMP_DIR}"

cat > "${TMP_DIR}/app.aos" <<'AOS'
Program#test_p1 {
  Import#test_i1(path="../../src/compiler/parser.aos")
  Export#test_e1(name=start)

  Let#test_l1(name=fail) {
    Fn#test_f1(params=message) {
      Block#test_b1 {
        Call#test_c1(target=sys.stdout.writeLine) { Var#test_v1(name=message) }
        Return#test_r1 { Lit#test_i1(value=1) }
      }
    }
  }

  Let#test_l2(name=start) {
    Fn#test_f2(params=args) {
      Block#test_b2 {
        Let#test_l3(name=sample) { Lit#test_i2(value="   Name#node") }
        Let#test_l4(name=startIndex) { Call#test_c2(target=parse.skipWhitespace) { Var#test_v2(name=sample) Lit#test_i3(value=0) } }
        If#test_if1 {
          Eq#test_e2 { Var#test_v3(name=startIndex) Lit#test_i4(value=3) }
          Block#test_b3 { Lit#test_i5(value=0) }
          Block#test_b4 { Return#test_r2 { Call#test_c3(target=fail) { Lit#test_i6(value="skipWhitespace failed") } } }
        }
        Let#test_l5(name=endIndex) { Call#test_c4(target=parse.readNameEnd) { Var#test_v4(name=sample) Var#test_v5(name=startIndex) } }
        If#test_if2 {
          Eq#test_e3 { Var#test_v6(name=endIndex) Lit#test_i7(value=7) }
          Block#test_b5 { Lit#test_i8(value=0) }
          Block#test_b6 { Return#test_r3 { Call#test_c5(target=fail) { Lit#test_i9(value="readNameEnd failed") } } }
        }
        If#test_if3 {
          Call#test_c6(target=parse.isDelimiter) { Call#test_c7(target=parse.charAt) { Var#test_v7(name=sample) Var#test_v8(name=endIndex) } }
          Block#test_b7 { Lit#test_i10(value=0) }
          Block#test_b8 { Return#test_r4 { Call#test_c8(target=fail) { Lit#test_i11(value="delimiter failed") } } }
        }
        Call#test_c9(target=sys.stdout.writeLine) { Lit#test_i12(value="parser-selfhost-ok") }
        Return#test_r5 { Lit#test_i13(value=0) }
      }
    }
  }
}
AOS

OUT="$(./tools/ailang run "${TMP_DIR}/app.aos")"
printf '%s\n' "${OUT}" | rg -Fq 'parser-selfhost-ok'
printf '%s\n' "${OUT}" | rg -Fq 'Ok#ok1(type=int value=0)'
