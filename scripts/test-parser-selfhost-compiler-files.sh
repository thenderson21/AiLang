#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

TMP_DIR="${ROOT_DIR}/.tmp/parser-selfhost-compiler-files"
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
        Let#test_l3(name=httpSource) {
          Call#test_c2(target=sys.bytes.toUtf8String) {
            Call#test_c3(target=sys.fs.file.read) {
              Lit#test_i2(value="src/compiler/http.aos")
            }
          }
        }
        Let#test_l4(name=httpProgram) { Call#test_c4(target=parse.parseNode) { Var#test_v2(name=httpSource) } }
        If#test_if1 {
          Eq#test_e2 { ChildCount#test_cc1 { Var#test_v3(name=httpProgram) } Lit#test_i3(value=2) }
          Block#test_b3 { Lit#test_i4(value=0) }
          Block#test_b4 { Return#test_r2 { Call#test_c5(target=fail) { Lit#test_i5(value="http compiler child count failed") } } }
        }
        Let#test_l5(name=httpMakeRequest) { ChildAt#test_ca1 { Var#test_v4(name=httpProgram) Lit#test_i6(value=1) } }
        Let#test_l6(name=httpFn) { ChildAt#test_ca2 { Var#test_v5(name=httpMakeRequest) Lit#test_i7(value=0) } }
        Let#test_l7(name=httpBlock) { ChildAt#test_ca3 { Var#test_v6(name=httpFn) Lit#test_i8(value=0) } }
        Let#test_l8(name=httpProgramTextLet) { ChildAt#test_ca4 { Var#test_v7(name=httpBlock) Lit#test_i9(value=0) } }
        Let#test_l9(name=httpConcat) { ChildAt#test_ca5 { Var#test_v8(name=httpProgramTextLet) Lit#test_i10(value=0) } }
        Let#test_l10(name=httpEmbeddedLit) { ChildAt#test_ca6 { Var#test_v9(name=httpConcat) Lit#test_i11(value=0) } }
        If#test_if2 {
          Eq#test_e3 { AttrValueString#test_avs1 { Var#test_v10(name=httpEmbeddedLit) Lit#test_i12(value=0) } Lit#test_i13(value="Program#hp_auto { HttpRequest#auto(method=\"") }
          Block#test_b5 { Lit#test_i14(value=0) }
          Block#test_b6 { Return#test_r3 { Call#test_c6(target=fail) { Lit#test_i15(value="http escaped literal failed") } } }
        }
        Call#test_c7(target=sys.stdout.writeLine) { Lit#test_i16(value="parser-selfhost-compiler-files-ok") }
        Return#test_r4 { Lit#test_i17(value=0) }
      }
    }
  }
}
AOS

OUT="$(./tools/ailang run "${TMP_DIR}/app.aos")"
printf '%s\n' "${OUT}" | rg -Fq 'parser-selfhost-compiler-files-ok'
printf '%s\n' "${OUT}" | rg -Fq 'Ok#ok1(type=int value=0)'
