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
        Let#test_l6(name=tok1) { Call#test_c9(target=parse.nextToken) { Var#test_v9(name=sample) Lit#test_i12(value=0) } }
        If#test_if4 {
          Eq#test_e4 { Call#test_c10(target=parse.tokenKind) { Var#test_v10(name=tok1) } Lit#test_i13(value="name") }
          Block#test_b9 { Lit#test_i14(value=0) }
          Block#test_b10 { Return#test_r5 { Call#test_c11(target=fail) { Lit#test_i15(value="token kind failed") } } }
        }
        If#test_if5 {
          Eq#test_e5 { Call#test_c12(target=parse.tokenValue) { Var#test_v11(name=tok1) } Lit#test_i16(value="Name") }
          Block#test_b11 { Lit#test_i17(value=0) }
          Block#test_b12 { Return#test_r6 { Call#test_c13(target=fail) { Lit#test_i18(value="token value failed") } } }
        }
        Let#test_l7(name=tok2) { Call#test_c14(target=parse.nextToken) { Var#test_v12(name=sample) Call#test_c15(target=parse.tokenNext) { Var#test_v13(name=tok1) } } }
        If#test_if6 {
          Eq#test_e6 { Call#test_c16(target=parse.tokenKind) { Var#test_v14(name=tok2) } Lit#test_i19(value="hash") }
          Block#test_b13 { Lit#test_i20(value=0) }
          Block#test_b14 { Return#test_r7 { Call#test_c17(target=fail) { Lit#test_i21(value="symbol token failed") } } }
        }
        Let#test_l8(name=programNode) { Call#test_c18(target=parse.parseEmptyNode) { Lit#test_i22(value="Program#p1 { }") } }
        If#test_if7 {
          Eq#test_e7 { NodeKind#test_nk1 { Var#test_v15(name=programNode) } Lit#test_i23(value="Program") }
          Block#test_b15 { Lit#test_i24(value=0) }
          Block#test_b16 { Return#test_r8 { Call#test_c19(target=fail) { Lit#test_i25(value="node kind failed") } } }
        }
        If#test_if8 {
          Eq#test_e8 { NodeId#test_nid1 { Var#test_v16(name=programNode) } Lit#test_i26(value="p1") }
          Block#test_b17 { Lit#test_i27(value=0) }
          Block#test_b18 { Return#test_r9 { Call#test_c20(target=fail) { Lit#test_i28(value="node id failed") } } }
        }
        Call#test_c21(target=sys.stdout.writeLine) { Lit#test_i29(value="parser-selfhost-ok") }
        Return#test_r10 { Lit#test_i30(value=0) }
      }
    }
  }
}
AOS

OUT="$(./tools/ailang run "${TMP_DIR}/app.aos")"
printf '%s\n' "${OUT}" | rg -Fq 'parser-selfhost-ok'
printf '%s\n' "${OUT}" | rg -Fq 'Ok#ok1(type=int value=0)'
