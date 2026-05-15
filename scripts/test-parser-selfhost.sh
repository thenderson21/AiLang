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
        Let#test_l9(name=exportNode) { Call#test_c21(target=parse.parseNodeWithNameAttr) { Lit#test_i29(value="Export#e1(name=start)") } }
        If#test_if9 {
          Eq#test_e9 { NodeKind#test_nk2 { Var#test_v17(name=exportNode) } Lit#test_i30(value="Export") }
          Block#test_b19 { Lit#test_i31(value=0) }
          Block#test_b20 { Return#test_r10 { Call#test_c22(target=fail) { Lit#test_i32(value="attr node kind failed") } } }
        }
        If#test_if10 {
          Eq#test_e10 { AttrCount#test_ac1 { Var#test_v18(name=exportNode) } Lit#test_i33(value=1) }
          Block#test_b21 { Lit#test_i34(value=0) }
          Block#test_b22 { Return#test_r11 { Call#test_c23(target=fail) { Lit#test_i35(value="attr count failed") } } }
        }
        If#test_if11 {
          Eq#test_e11 { AttrKey#test_ak1 { Var#test_v19(name=exportNode) Lit#test_i36(value=0) } Lit#test_i37(value="name") }
          Block#test_b23 { Lit#test_i38(value=0) }
          Block#test_b24 { Return#test_r12 { Call#test_c24(target=fail) { Lit#test_i39(value="attr key failed") } } }
        }
        If#test_if12 {
          Eq#test_e12 { AttrValueString#test_avs1 { Var#test_v20(name=exportNode) Lit#test_i40(value=0) } Lit#test_i41(value="start") }
          Block#test_b25 { Lit#test_i42(value=0) }
          Block#test_b26 { Return#test_r13 { Call#test_c25(target=fail) { Lit#test_i43(value="attr value failed") } } }
        }
        Let#test_l10(name=programTree) { Call#test_c26(target=parse.parseProgramWithOneChild) { Lit#test_i44(value="Program#p2 { Export#e2(name=start) }") } }
        If#test_if13 {
          Eq#test_e13 { ChildCount#test_cc1 { Var#test_v21(name=programTree) } Lit#test_i45(value=1) }
          Block#test_b27 { Lit#test_i46(value=0) }
          Block#test_b28 { Return#test_r14 { Call#test_c27(target=fail) { Lit#test_i47(value="program child count failed") } } }
        }
        Let#test_l11(name=childNode) { ChildAt#test_ca1 { Var#test_v22(name=programTree) Lit#test_i48(value=0) } }
        If#test_if14 {
          Eq#test_e14 { NodeKind#test_nk3 { Var#test_v23(name=childNode) } Lit#test_i49(value="Export") }
          Block#test_b29 { Lit#test_i50(value=0) }
          Block#test_b30 { Return#test_r15 { Call#test_c28(target=fail) { Lit#test_i51(value="program child kind failed") } } }
        }
        If#test_if15 {
          Eq#test_e15 { AttrValueString#test_avs2 { Var#test_v24(name=childNode) Lit#test_i52(value=0) } Lit#test_i53(value="start") }
          Block#test_b31 { Lit#test_i54(value=0) }
          Block#test_b32 { Return#test_r16 { Call#test_c29(target=fail) { Lit#test_i55(value="program child attr failed") } } }
        }
        Let#test_l12(name=twoChildProgram) {
          Call#test_c30(target=parse.parseNode) {
            Lit#test_i56(value="Program#p3 { Export#e3(name=start) Export#e4(name=stop) }")
          }
        }
        If#test_if16 {
          Eq#test_e16 { ChildCount#test_cc2 { Var#test_v25(name=twoChildProgram) } Lit#test_i57(value=2) }
          Block#test_b33 { Lit#test_i58(value=0) }
          Block#test_b34 { Return#test_r17 { Call#test_c31(target=fail) { Lit#test_i59(value="two child count failed") } } }
        }
        Let#test_l13(name=secondChild) { ChildAt#test_ca2 { Var#test_v26(name=twoChildProgram) Lit#test_i60(value=1) } }
        If#test_if17 {
          Eq#test_e17 { NodeId#test_nid2 { Var#test_v27(name=secondChild) } Lit#test_i61(value="e4") }
          Block#test_b35 { Lit#test_i62(value=0) }
          Block#test_b36 { Return#test_r18 { Call#test_c32(target=fail) { Lit#test_i63(value="second child id failed") } } }
        }
        If#test_if18 {
          Eq#test_e18 { AttrValueString#test_avs3 { Var#test_v28(name=secondChild) Lit#test_i64(value=0) } Lit#test_i65(value="stop") }
          Block#test_b37 { Lit#test_i66(value=0) }
          Block#test_b38 { Return#test_r19 { Call#test_c33(target=fail) { Lit#test_i67(value="second child attr failed") } } }
        }
        Let#test_l14(name=nestedProgram) {
          Call#test_c34(target=parse.parseNode) {
            Lit#test_i68(value="Program#p4 { Let#l4 { Export#e5(name=start) } }")
          }
        }
        Let#test_l15(name=letNode) { ChildAt#test_ca3 { Var#test_v29(name=nestedProgram) Lit#test_i69(value=0) } }
        If#test_if19 {
          Eq#test_e19 { NodeKind#test_nk4 { Var#test_v30(name=letNode) } Lit#test_i70(value="Let") }
          Block#test_b39 { Lit#test_i71(value=0) }
          Block#test_b40 { Return#test_r20 { Call#test_c35(target=fail) { Lit#test_i72(value="nested let kind failed") } } }
        }
        If#test_if20 {
          Eq#test_e20 { ChildCount#test_cc3 { Var#test_v31(name=letNode) } Lit#test_i73(value=1) }
          Block#test_b41 { Lit#test_i74(value=0) }
          Block#test_b42 { Return#test_r21 { Call#test_c36(target=fail) { Lit#test_i75(value="nested child count failed") } } }
        }
        Let#test_l16(name=nestedExport) { ChildAt#test_ca4 { Var#test_v32(name=letNode) Lit#test_i76(value=0) } }
        If#test_if21 {
          Eq#test_e21 { AttrValueString#test_avs4 { Var#test_v33(name=nestedExport) Lit#test_i77(value=0) } Lit#test_i78(value="start") }
          Block#test_b43 { Lit#test_i79(value=0) }
          Block#test_b44 { Return#test_r22 { Call#test_c37(target=fail) { Lit#test_i80(value="nested export attr failed") } } }
        }
        Let#test_l17(name=quotedToken) {
          Call#test_c38(target=parse.nextToken) {
            Lit#test_i81(value="\"./app.aos\"")
            Lit#test_i82(value=0)
          }
        }
        If#test_if22 {
          Eq#test_e22 { Call#test_c39(target=parse.tokenKind) { Var#test_v34(name=quotedToken) } Lit#test_i83(value="string") }
          Block#test_b45 { Lit#test_i84(value=0) }
          Block#test_b46 { Return#test_r23 { Call#test_c40(target=fail) { Lit#test_i85(value="quoted token kind failed") } } }
        }
        If#test_if23 {
          Eq#test_e23 { Call#test_c41(target=parse.tokenValue) { Var#test_v35(name=quotedToken) } Lit#test_i86(value="./app.aos") }
          Block#test_b47 { Lit#test_i87(value=0) }
          Block#test_b48 { Return#test_r24 { Call#test_c42(target=fail) { Lit#test_i88(value="quoted token value failed") } } }
        }
        Let#test_l18(name=importNode) {
          Call#test_c43(target=parse.parseNode) {
            Lit#test_i89(value="Import#i1(path=\"./app.aos\")")
          }
        }
        If#test_if24 {
          Eq#test_e24 { AttrValueString#test_avs5 { Var#test_v36(name=importNode) Lit#test_i90(value=0) } Lit#test_i91(value="./app.aos") }
          Block#test_b49 { Lit#test_i92(value=0) }
          Block#test_b50 { Return#test_r25 { Call#test_c44(target=fail) { Lit#test_i93(value="quoted attr value failed") } } }
        }
        Let#test_l19(name=boolNode) {
          Call#test_c45(target=parse.parseNode) {
            Lit#test_i94(value="Flag#f1(enabled=true)")
          }
        }
        If#test_if25 {
          Eq#test_e25 { AttrValueBool#test_avb1 { Var#test_v37(name=boolNode) Lit#test_i95(value=0) } Lit#test_i96(value=true) }
          Block#test_b51 { Lit#test_i97(value=0) }
          Block#test_b52 { Return#test_r26 { Call#test_c46(target=fail) { Lit#test_i98(value="bool attr value failed") } } }
        }
        Let#test_l20(name=intNode) {
          Call#test_c47(target=parse.parseNode) {
            Lit#test_i99(value="Window#w1(width=320)")
          }
        }
        If#test_if26 {
          Eq#test_e26 { AttrValueInt#test_avi1 { Var#test_v38(name=intNode) Lit#test_i100(value=0) } Lit#test_i101(value=320) }
          Block#test_b53 { Lit#test_i102(value=0) }
          Block#test_b54 { Return#test_r27 { Call#test_c48(target=fail) { Lit#test_i103(value="int attr value failed") } } }
        }
        Let#test_l21(name=multiAttrNode) {
          Call#test_c49(target=parse.parseNode) {
            Lit#test_i104(value="Bytecode#bc1(flags=0 format=\"AiBC1\" magic=\"AIBC\" version=2)")
          }
        }
        If#test_if27 {
          Eq#test_e27 { AttrCount#test_ac2 { Var#test_v39(name=multiAttrNode) } Lit#test_i105(value=4) }
          Block#test_b55 { Lit#test_i106(value=0) }
          Block#test_b56 { Return#test_r28 { Call#test_c50(target=fail) { Lit#test_i107(value="multi attr count failed") } } }
        }
        If#test_if28 {
          Eq#test_e28 { AttrValueString#test_avs6 { Var#test_v40(name=multiAttrNode) Lit#test_i108(value=1) } Lit#test_i109(value="AiBC1") }
          Block#test_b57 { Lit#test_i110(value=0) }
          Block#test_b58 { Return#test_r29 { Call#test_c51(target=fail) { Lit#test_i111(value="multi attr string failed") } } }
        }
        If#test_if29 {
          Eq#test_e29 { AttrValueInt#test_avi2 { Var#test_v41(name=multiAttrNode) Lit#test_i112(value=3) } Lit#test_i113(value=2) }
          Block#test_b59 { Lit#test_i114(value=0) }
          Block#test_b60 { Return#test_r30 { Call#test_c52(target=fail) { Lit#test_i115(value="multi attr int failed") } } }
        }
        Call#test_c53(target=sys.stdout.writeLine) { Lit#test_i116(value="parser-selfhost-ok") }
        Return#test_r31 { Lit#test_i117(value=0) }
      }
    }
  }
}
AOS

OUT="$(./tools/ailang run "${TMP_DIR}/app.aos")"
printf '%s\n' "${OUT}" | rg -Fq 'parser-selfhost-ok'
printf '%s\n' "${OUT}" | rg -Fq 'Ok#ok1(type=int value=0)'
