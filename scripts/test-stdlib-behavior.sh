#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

TMP_DIR="${ROOT_DIR}/.tmp/stdlib-behavior"
rm -rf "${TMP_DIR}"
mkdir -p "${TMP_DIR}"

cat > "${TMP_DIR}/number-null.aos" <<'AOS'
Program#stdlib_behavior_number_p1 {
  Import#stdlib_behavior_number_i1(path="../../src/std/number.aos")
  Import#stdlib_behavior_number_i2(path="../../src/std/null.aos")
  Import#stdlib_behavior_number_i3(path="../../src/std/io.aos")
  Export#stdlib_behavior_number_e1(name=start)

  Let#stdlib_behavior_number_l1(name=start) {
    Fn#stdlib_behavior_number_f1(params=args) {
      Block#stdlib_behavior_number_b1 {
        Call#stdlib_behavior_number_c1(target=io.print) {
          Call#stdlib_behavior_number_c2(target=toString) {
            Call#stdlib_behavior_number_c3(target=negate) { Lit#stdlib_behavior_number_i1(value=7) }
          }
        }
        Call#stdlib_behavior_number_c4(target=io.print) {
          Call#stdlib_behavior_number_c5(target=toString) {
            Call#stdlib_behavior_number_c6(target=negate) { Lit#stdlib_behavior_number_i2(value=-7) }
          }
        }
        Call#stdlib_behavior_number_c7(target=io.print) {
          Call#stdlib_behavior_number_c8(target=toString) {
            Call#stdlib_behavior_number_c9(target=sub) { Lit#stdlib_behavior_number_i3(value=10) Lit#stdlib_behavior_number_i4(value=3) }
          }
        }
        Call#stdlib_behavior_number_c10(target=io.print) {
          Call#stdlib_behavior_number_c11(target=toString) {
            Call#stdlib_behavior_number_c12(target=sub) { Lit#stdlib_behavior_number_i5(value=3) Lit#stdlib_behavior_number_i6(value=10) }
          }
        }
        Call#stdlib_behavior_number_c13(target=io.print) {
          Call#stdlib_behavior_number_c14(target=toString) {
            Call#stdlib_behavior_number_c15(target=mul) { Lit#stdlib_behavior_number_i7(value=6) Lit#stdlib_behavior_number_i8(value=7) }
          }
        }
        Call#stdlib_behavior_number_c16(target=io.print) {
          Call#stdlib_behavior_number_c17(target=toString) {
            Call#stdlib_behavior_number_c18(target=mul) { Lit#stdlib_behavior_number_i9(value=6) Lit#stdlib_behavior_number_i10(value=-7) }
          }
        }
        Call#stdlib_behavior_number_c19(target=io.print) {
          ToString#stdlib_behavior_number_t1 {
            Call#stdlib_behavior_number_c20(target=isNumberString) { Lit#stdlib_behavior_number_i11(value="-42") }
          }
        }
        Call#stdlib_behavior_number_c21(target=io.print) {
          Call#stdlib_behavior_number_c22(target=toString) {
            Call#stdlib_behavior_number_c23(target=parseNumberOr) { Lit#stdlib_behavior_number_i12(value="-42") Lit#stdlib_behavior_number_i13(value=7) }
          }
        }
        Call#stdlib_behavior_number_c24(target=io.print) {
          Call#stdlib_behavior_number_c25(target=toString) {
            Call#stdlib_behavior_number_c26(target=parseNumberOr) { Lit#stdlib_behavior_number_i14(value="-") Lit#stdlib_behavior_number_i15(value=7) }
          }
        }
        Call#stdlib_behavior_number_c27(target=io.print) {
          Call#stdlib_behavior_number_c28(target=coalesce) {
            Call#stdlib_behavior_number_c29(target=value) { Lit#stdlib_behavior_number_i16(value=0) }
            Lit#stdlib_behavior_number_i17(value="fallback")
          }
        }
        Return#stdlib_behavior_number_r1 { Lit#stdlib_behavior_number_i18(value=0) }
      }
    }
  }
}
AOS

cat > "${TMP_DIR}/math.aos" <<'AOS'
Program#stdlib_behavior_math_p1 {
  Import#stdlib_behavior_math_i1(path="../../src/std/math.aos")
  Import#stdlib_behavior_math_i2(path="../../src/std/io.aos")
  Export#stdlib_behavior_math_e1(name=start)

  Let#stdlib_behavior_math_l1(name=start) {
    Fn#stdlib_behavior_math_f1(params=args) {
      Block#stdlib_behavior_math_b1 {
        Call#stdlib_behavior_math_c1(target=io.print) {
          ToString#stdlib_behavior_math_t1 {
            Call#stdlib_behavior_math_c2(target=add) { Lit#stdlib_behavior_math_i1(value=2) Lit#stdlib_behavior_math_i2(value=3) }
          }
        }
        Call#stdlib_behavior_math_c3(target=io.print) {
          ToString#stdlib_behavior_math_t2 {
            Call#stdlib_behavior_math_c4(target=sub) { Lit#stdlib_behavior_math_i3(value=2) Lit#stdlib_behavior_math_i4(value=9) }
          }
        }
        Call#stdlib_behavior_math_c5(target=io.print) {
          ToString#stdlib_behavior_math_t3 {
            Call#stdlib_behavior_math_c6(target=mul) { Lit#stdlib_behavior_math_i5(value=-3) Lit#stdlib_behavior_math_i6(value=4) }
          }
        }
        Call#stdlib_behavior_math_c7(target=io.print) {
          ToString#stdlib_behavior_math_t4 {
            Call#stdlib_behavior_math_c8(target=negate) { Lit#stdlib_behavior_math_i7(value=-12) }
          }
        }
        Return#stdlib_behavior_math_r1 { Lit#stdlib_behavior_math_i8(value=0) }
      }
    }
  }
}
AOS

cat > "${TMP_DIR}/core-str.aos" <<'AOS'
Program#stdlib_behavior_core_str_p1 {
  Import#stdlib_behavior_core_str_i1(path="../../src/std/core.aos")
  Import#stdlib_behavior_core_str_i2(path="../../src/std/str.aos")
  Import#stdlib_behavior_core_str_i3(path="../../src/std/io.aos")
  Export#stdlib_behavior_core_str_e1(name=start)

  Let#stdlib_behavior_core_str_l1(name=start) {
    Fn#stdlib_behavior_core_str_f1(params=args) {
      Block#stdlib_behavior_core_str_b1 {
        Let#stdlib_behavior_core_str_l2(name=ok) {
          Call#stdlib_behavior_core_str_c1(target=resultOkString) {
            Lit#stdlib_behavior_core_str_i1(value="value")
          }
        }
        Let#stdlib_behavior_core_str_l3(name=err) {
          Call#stdlib_behavior_core_str_c2(target=resultErr) {
            Lit#stdlib_behavior_core_str_i2(value="E_TEST")
            Lit#stdlib_behavior_core_str_i3(value="failed")
          }
        }
        Let#stdlib_behavior_core_str_l4(name=some) {
          Call#stdlib_behavior_core_str_c3(target=optionSomeString) {
            Lit#stdlib_behavior_core_str_i4(value="present")
          }
        }
        Let#stdlib_behavior_core_str_l5(name=none) {
          Call#stdlib_behavior_core_str_c4(target=optionNone) {
            Lit#stdlib_behavior_core_str_i5(value=0)
          }
        }
        Call#stdlib_behavior_core_str_c8(target=io.print) {
          ToString#stdlib_behavior_core_str_t1 {
            Call#stdlib_behavior_core_str_c9(target=resultIsOk) {
              Var#stdlib_behavior_core_str_v3(name=ok)
            }
          }
        }
        Call#stdlib_behavior_core_str_c10(target=io.print) {
          Call#stdlib_behavior_core_str_c11(target=resultValueOr) {
            Var#stdlib_behavior_core_str_v4(name=ok)
            Lit#stdlib_behavior_core_str_i8(value="fallback")
          }
        }
        Call#stdlib_behavior_core_str_c12(target=io.print) {
          Call#stdlib_behavior_core_str_c13(target=resultCodeOr) {
            Var#stdlib_behavior_core_str_v5(name=err)
            Lit#stdlib_behavior_core_str_i9(value="NO_CODE")
          }
        }
        Call#stdlib_behavior_core_str_c14(target=io.print) {
          Call#stdlib_behavior_core_str_c15(target=resultMessageOr) {
            Var#stdlib_behavior_core_str_v6(name=err)
            Lit#stdlib_behavior_core_str_i10(value="NO_MESSAGE")
          }
        }
        Call#stdlib_behavior_core_str_c16(target=io.print) {
          ToString#stdlib_behavior_core_str_t2 {
            Call#stdlib_behavior_core_str_c17(target=optionHas) {
              Var#stdlib_behavior_core_str_v7(name=some)
            }
          }
        }
        Call#stdlib_behavior_core_str_c18(target=io.print) {
          Call#stdlib_behavior_core_str_c19(target=optionValueOr) {
            Var#stdlib_behavior_core_str_v8(name=none)
            Lit#stdlib_behavior_core_str_i11(value="empty")
          }
        }
        Call#stdlib_behavior_core_str_c20(target=io.print) {
          Call#stdlib_behavior_core_str_c21(target=concat) {
            Lit#stdlib_behavior_core_str_i12(value="ai")
            Lit#stdlib_behavior_core_str_i13(value="lang")
          }
        }
        Call#stdlib_behavior_core_str_c22(target=io.print) {
          Call#stdlib_behavior_core_str_c23(target=substring) {
            Lit#stdlib_behavior_core_str_i14(value="abcdef")
            Lit#stdlib_behavior_core_str_i15(value=2)
            Lit#stdlib_behavior_core_str_i16(value=3)
          }
        }
        Call#stdlib_behavior_core_str_c24(target=io.print) {
          Call#stdlib_behavior_core_str_c25(target=remove) {
            Lit#stdlib_behavior_core_str_i17(value="abcdef")
            Lit#stdlib_behavior_core_str_i18(value=2)
            Lit#stdlib_behavior_core_str_i19(value=2)
          }
        }
        Call#stdlib_behavior_core_str_c26(target=io.print) {
          ToString#stdlib_behavior_core_str_t3 {
            Call#stdlib_behavior_core_str_c27(target=find) {
              Lit#stdlib_behavior_core_str_i20(value="abcabc")
              Lit#stdlib_behavior_core_str_i21(value="ca")
              Lit#stdlib_behavior_core_str_i22(value=0)
            }
          }
        }
        Call#stdlib_behavior_core_str_c28(target=io.print) {
          Call#stdlib_behavior_core_str_c29(target=replaceAll) {
            Lit#stdlib_behavior_core_str_i23(value="one fish one fish")
            Lit#stdlib_behavior_core_str_i24(value="fish")
            Lit#stdlib_behavior_core_str_i25(value="cat")
          }
        }
        Call#stdlib_behavior_core_str_c30(target=io.print) {
          Call#stdlib_behavior_core_str_c31(target=fromCodePoint) {
            Lit#stdlib_behavior_core_str_i26(value=65)
          }
        }
        Return#stdlib_behavior_core_str_r1 { Lit#stdlib_behavior_core_str_i31(value=0) }
      }
    }
  }
}
AOS

cat > "${TMP_DIR}/bytes.aos" <<'AOS'
Program#stdlib_behavior_bytes_p1 {
  Import#stdlib_behavior_bytes_i1(path="../../src/std/bytes.aos")
  Import#stdlib_behavior_bytes_i2(path="../../src/std/io.aos")
  Export#stdlib_behavior_bytes_e1(name=start)

  Let#stdlib_behavior_bytes_l1(name=start) {
    Fn#stdlib_behavior_bytes_f1(params=args) {
      Block#stdlib_behavior_bytes_b1 {
        Let#stdlib_behavior_bytes_l2(name=data) {
          Call#stdlib_behavior_bytes_c1(target=fromUtf8String) {
            Lit#stdlib_behavior_bytes_i1(value="hello")
          }
        }
        Let#stdlib_behavior_bytes_l3(name=tail) {
          Call#stdlib_behavior_bytes_c2(target=fromUtf8String) {
            Lit#stdlib_behavior_bytes_i2(value="!")
          }
        }
        Let#stdlib_behavior_bytes_l4(name=joinedBytes) {
          Call#stdlib_behavior_bytes_c3(target=concat) {
            Var#stdlib_behavior_bytes_v1(name=data)
            Var#stdlib_behavior_bytes_v2(name=tail)
          }
        }
        Call#stdlib_behavior_bytes_c4(target=io.print) {
          ToString#stdlib_behavior_bytes_t1 {
            Call#stdlib_behavior_bytes_c5(target=length) {
              Var#stdlib_behavior_bytes_v3(name=data)
            }
          }
        }
        Call#stdlib_behavior_bytes_c6(target=io.print) {
          ToString#stdlib_behavior_bytes_t2 {
            Call#stdlib_behavior_bytes_c7(target=at) {
              Var#stdlib_behavior_bytes_v4(name=data)
              Lit#stdlib_behavior_bytes_i3(value=1)
            }
          }
        }
        Call#stdlib_behavior_bytes_c8(target=io.print) {
          Call#stdlib_behavior_bytes_c9(target=toUtf8String) {
            Call#stdlib_behavior_bytes_c10(target=slice) {
              Var#stdlib_behavior_bytes_v5(name=joinedBytes)
              Lit#stdlib_behavior_bytes_i4(value=1)
              Lit#stdlib_behavior_bytes_i5(value=4)
            }
          }
        }
        Call#stdlib_behavior_bytes_c11(target=io.print) {
          Call#stdlib_behavior_bytes_c12(target=toBase64) {
            Var#stdlib_behavior_bytes_v6(name=data)
          }
        }
        Call#stdlib_behavior_bytes_c13(target=io.print) {
          Call#stdlib_behavior_bytes_c14(target=toUtf8String) {
            Call#stdlib_behavior_bytes_c15(target=fromBase64) {
              Lit#stdlib_behavior_bytes_i6(value="aGVsbG8=")
            }
          }
        }
        Return#stdlib_behavior_bytes_r1 { Lit#stdlib_behavior_bytes_i7(value=0) }
      }
    }
  }
}
AOS

cat > "${TMP_DIR}/io-debug.aos" <<'AOS'
Program#stdlib_behavior_io_debug_p1 {
  Import#stdlib_behavior_io_debug_i1(path="../../src/std/io.aos")
  Import#stdlib_behavior_io_debug_i2(path="../../src/std/debug.aos")
  Export#stdlib_behavior_io_debug_e1(name=start)

  Let#stdlib_behavior_io_debug_l1(name=start) {
    Fn#stdlib_behavior_io_debug_f1(params=args) {
      Block#stdlib_behavior_io_debug_b1 {
        Call#stdlib_behavior_io_debug_c1(target=write) {
          Lit#stdlib_behavior_io_debug_i1(value="out-a")
        }
        Call#stdlib_behavior_io_debug_c2(target=writeLine) {
          Lit#stdlib_behavior_io_debug_i2(value="out-b")
        }
        Call#stdlib_behavior_io_debug_c3(target=writeErrLine) {
          Lit#stdlib_behavior_io_debug_i3(value="err-a")
        }
        Call#stdlib_behavior_io_debug_c4(target=info) {
          Lit#stdlib_behavior_io_debug_i4(value="hello")
        }
        Call#stdlib_behavior_io_debug_c5(target=warn) {
          Lit#stdlib_behavior_io_debug_i5(value="careful")
        }
        Call#stdlib_behavior_io_debug_c6(target=error) {
          Lit#stdlib_behavior_io_debug_i6(value="bad")
        }
        Call#stdlib_behavior_io_debug_c7(target=log) {
          Lit#stdlib_behavior_io_debug_i7(value="audit")
          Lit#stdlib_behavior_io_debug_i8(value="trail")
        }
        Call#stdlib_behavior_io_debug_c8(target=debugAssert) {
          Lit#stdlib_behavior_io_debug_i9(value=true)
          Lit#stdlib_behavior_io_debug_i10(value="ASSERT_OK")
          Lit#stdlib_behavior_io_debug_i11(value="assertion passed")
        }
        Return#stdlib_behavior_io_debug_r1 { Lit#stdlib_behavior_io_debug_i12(value=0) }
      }
    }
  }
}
AOS

cat > "${TMP_DIR}/fs.aos" <<AOS
Program#stdlib_behavior_fs_p1 {
  Import#stdlib_behavior_fs_i1(path="../../src/std/fs.aos")
  Import#stdlib_behavior_fs_i2(path="../../src/std/bytes.aos")
  Import#stdlib_behavior_fs_i3(path="../../src/std/io.aos")
  Export#stdlib_behavior_fs_e1(name=start)

  Let#stdlib_behavior_fs_l1(name=start) {
    Fn#stdlib_behavior_fs_f1(params=args) {
      Block#stdlib_behavior_fs_b1 {
        Let#stdlib_behavior_fs_l2(name=filePath) {
          Lit#stdlib_behavior_fs_s1(value="${TMP_DIR}/fs-behavior.txt")
        }
        Let#stdlib_behavior_fs_l3(name=dirPath) {
          Lit#stdlib_behavior_fs_s2(value="${TMP_DIR}/fs-behavior-dir")
        }
        Call#stdlib_behavior_fs_c1(target=fileWrite) {
          Var#stdlib_behavior_fs_v1(name=filePath)
          Call#stdlib_behavior_fs_c2(target=fromUtf8String) {
            Lit#stdlib_behavior_fs_s3(value="fs-ok")
          }
        }
        Call#stdlib_behavior_fs_c3(target=io.print) {
          ToString#stdlib_behavior_fs_t1 {
            Call#stdlib_behavior_fs_c4(target=fileExists) {
              Var#stdlib_behavior_fs_v2(name=filePath)
            }
          }
        }
        Call#stdlib_behavior_fs_c5(target=io.print) {
          ToString#stdlib_behavior_fs_t2 {
            Call#stdlib_behavior_fs_c6(target=pathExists) {
              Var#stdlib_behavior_fs_v3(name=filePath)
            }
          }
        }
        Call#stdlib_behavior_fs_c7(target=io.print) {
          Call#stdlib_behavior_fs_c8(target=toUtf8String) {
            Call#stdlib_behavior_fs_c9(target=fileRead) {
              Var#stdlib_behavior_fs_v4(name=filePath)
            }
          }
        }
        Call#stdlib_behavior_fs_c10(target=fileDelete) {
          Var#stdlib_behavior_fs_v5(name=filePath)
        }
        Call#stdlib_behavior_fs_c11(target=io.print) {
          ToString#stdlib_behavior_fs_t3 {
            Call#stdlib_behavior_fs_c12(target=fileExists) {
              Var#stdlib_behavior_fs_v6(name=filePath)
            }
          }
        }
        Call#stdlib_behavior_fs_c13(target=dirCreate) {
          Var#stdlib_behavior_fs_v7(name=dirPath)
        }
        Call#stdlib_behavior_fs_c14(target=io.print) {
          ToString#stdlib_behavior_fs_t4 {
            Call#stdlib_behavior_fs_c15(target=pathExists) {
              Var#stdlib_behavior_fs_v8(name=dirPath)
            }
          }
        }
        Call#stdlib_behavior_fs_c16(target=dirDelete) {
          Var#stdlib_behavior_fs_v9(name=dirPath)
          Lit#stdlib_behavior_fs_b2(value=true)
        }
        Call#stdlib_behavior_fs_c17(target=io.print) {
          ToString#stdlib_behavior_fs_t5 {
            Call#stdlib_behavior_fs_c18(target=pathExists) {
              Var#stdlib_behavior_fs_v10(name=dirPath)
            }
          }
        }
        Return#stdlib_behavior_fs_r1 { Lit#stdlib_behavior_fs_i1(value=0) }
      }
    }
  }
}
AOS

cat > "${TMP_DIR}/process-system.aos" <<'AOS'
Program#stdlib_behavior_process_system_p1 {
  Import#stdlib_behavior_process_system_i1(path="../../src/std/process.aos")
  Import#stdlib_behavior_process_system_i2(path="../../src/std/system.aos")
  Import#stdlib_behavior_process_system_i3(path="../../src/std/io.aos")
  Export#stdlib_behavior_process_system_e1(name=start)

  Let#stdlib_behavior_process_system_l1(name=start) {
    Fn#stdlib_behavior_process_system_f1(params=args) {
      Block#stdlib_behavior_process_system_b1 {
        Call#stdlib_behavior_process_system_c1(target=io.print) {
          Call#stdlib_behavior_process_system_c2(target=cwd) {
            Lit#stdlib_behavior_process_system_i1(value=0)
          }
        }
        Call#stdlib_behavior_process_system_c3(target=io.print) {
          Call#stdlib_behavior_process_system_c4(target=envGet) {
            Lit#stdlib_behavior_process_system_s1(value="AILANG_STDLIB_BEHAVIOR_ENV")
          }
        }
        Call#stdlib_behavior_process_system_c5(target=io.print) {
          Call#stdlib_behavior_process_system_c6(target=platform) {
            Lit#stdlib_behavior_process_system_i2(value=0)
          }
        }
        Call#stdlib_behavior_process_system_c7(target=io.print) {
          Call#stdlib_behavior_process_system_c8(target=arch) {
            Lit#stdlib_behavior_process_system_i3(value=0)
          }
        }
        Call#stdlib_behavior_process_system_c9(target=io.print) {
          Call#stdlib_behavior_process_system_c10(target=runtime) {
            Lit#stdlib_behavior_process_system_i4(value=0)
          }
        }
        Return#stdlib_behavior_process_system_r1 {
          Lit#stdlib_behavior_process_system_i5(value=0)
        }
      }
    }
  }
}
AOS

cat > "${TMP_DIR}/time.aos" <<'AOS'
Program#stdlib_behavior_time_p1 {
  Import#stdlib_behavior_time_i1(path="../../src/std/time.aos")
  Import#stdlib_behavior_time_i2(path="../../src/std/io.aos")
  Export#stdlib_behavior_time_e1(name=start)

  Let#stdlib_behavior_time_l1(name=start) {
    Fn#stdlib_behavior_time_f1(params=args) {
      Block#stdlib_behavior_time_b1 {
        Let#stdlib_behavior_time_l2(name=utc) {
          Call#stdlib_behavior_time_c1(target=timeUtc) {
            Lit#stdlib_behavior_time_i1(value=12345)
          }
        }
        Call#stdlib_behavior_time_c2(target=io.print) {
          ToString#stdlib_behavior_time_t1 {
            Call#stdlib_behavior_time_c3(target=timeUnixMs) {
              Var#stdlib_behavior_time_v1(name=utc)
            }
          }
        }
        Call#stdlib_behavior_time_c4(target=io.print) {
          Call#stdlib_behavior_time_c5(target=timeZone) {
            Var#stdlib_behavior_time_v2(name=utc)
          }
        }
        Call#stdlib_behavior_time_c6(target=io.print) {
          ToString#stdlib_behavior_time_t2 {
            Call#stdlib_behavior_time_c7(target=timeOffsetMinutes) {
              Var#stdlib_behavior_time_v3(name=utc)
            }
          }
        }
        Call#stdlib_behavior_time_c8(target=io.print) {
          ToString#stdlib_behavior_time_t3 {
            Call#stdlib_behavior_time_c9(target=timeIsUtc) {
              Var#stdlib_behavior_time_v4(name=utc)
            }
          }
        }
        Return#stdlib_behavior_time_r1 { Lit#stdlib_behavior_time_i2(value=0) }
      }
    }
  }
}
AOS

NUMBER_NULL_OUT="$("${ROOT_DIR}/tools/ailang" run "${TMP_DIR}/number-null.aos")"
NUMBER_NULL_EXPECTED='-7
7
7
-7
42
-42
true
-42
7
fallback
Ok#ok1(type=int value=0)'

if [[ "${NUMBER_NULL_OUT}" != "${NUMBER_NULL_EXPECTED}" ]]; then
  echo "stdlib number/null behavior mismatch" >&2
  echo "expected:" >&2
  printf '%s\n' "${NUMBER_NULL_EXPECTED}" >&2
  echo "actual:" >&2
  printf '%s\n' "${NUMBER_NULL_OUT}" >&2
  exit 1
fi

MATH_OUT="$("${ROOT_DIR}/tools/ailang" run "${TMP_DIR}/math.aos")"
MATH_EXPECTED='5
-7
-12
12
Ok#ok1(type=int value=0)'

if [[ "${MATH_OUT}" != "${MATH_EXPECTED}" ]]; then
  echo "stdlib math behavior mismatch" >&2
  echo "expected:" >&2
  printf '%s\n' "${MATH_EXPECTED}" >&2
  echo "actual:" >&2
  printf '%s\n' "${MATH_OUT}" >&2
  exit 1
fi

CORE_STR_OUT="$("${ROOT_DIR}/tools/ailang" run "${TMP_DIR}/core-str.aos")"
CORE_STR_EXPECTED='true
value
E_TEST
failed
true
empty
ailang
cde
abef
2
one cat one cat
A
Ok#ok1(type=int value=0)'

if [[ "${CORE_STR_OUT}" != "${CORE_STR_EXPECTED}" ]]; then
  echo "stdlib core/str behavior mismatch" >&2
  echo "expected:" >&2
  printf '%s\n' "${CORE_STR_EXPECTED}" >&2
  echo "actual:" >&2
  printf '%s\n' "${CORE_STR_OUT}" >&2
  exit 1
fi

BYTES_OUT="$("${ROOT_DIR}/tools/ailang" run "${TMP_DIR}/bytes.aos")"
BYTES_EXPECTED='5
101
ello
aGVsbG8=
hello
Ok#ok1(type=int value=0)'

if [[ "${BYTES_OUT}" != "${BYTES_EXPECTED}" ]]; then
  echo "stdlib bytes behavior mismatch" >&2
  echo "expected:" >&2
  printf '%s\n' "${BYTES_EXPECTED}" >&2
  echo "actual:" >&2
  printf '%s\n' "${BYTES_OUT}" >&2
  exit 1
fi

IO_DEBUG_STDOUT="${TMP_DIR}/io-debug.stdout"
IO_DEBUG_STDERR="${TMP_DIR}/io-debug.stderr"
"${ROOT_DIR}/tools/ailang" run "${TMP_DIR}/io-debug.aos" >"${IO_DEBUG_STDOUT}" 2>"${IO_DEBUG_STDERR}"

IO_DEBUG_STDOUT_EXPECTED='out-aout-b
Ok#ok1(type=int value=0)'
IO_DEBUG_STDERR_EXPECTED='err-a
[info] hello
[warn] careful
[error] bad
[audit] trail'

IO_DEBUG_STDOUT_ACTUAL="$(cat "${IO_DEBUG_STDOUT}")"
IO_DEBUG_STDERR_ACTUAL="$(cat "${IO_DEBUG_STDERR}")"

if [[ "${IO_DEBUG_STDOUT_ACTUAL}" != "${IO_DEBUG_STDOUT_EXPECTED}" ]]; then
  echo "stdlib io/debug stdout behavior mismatch" >&2
  echo "expected:" >&2
  printf '%s\n' "${IO_DEBUG_STDOUT_EXPECTED}" >&2
  echo "actual:" >&2
  printf '%s\n' "${IO_DEBUG_STDOUT_ACTUAL}" >&2
  exit 1
fi

if [[ "${IO_DEBUG_STDERR_ACTUAL}" != "${IO_DEBUG_STDERR_EXPECTED}" ]]; then
  echo "stdlib io/debug stderr behavior mismatch" >&2
  echo "expected:" >&2
  printf '%s\n' "${IO_DEBUG_STDERR_EXPECTED}" >&2
  echo "actual:" >&2
  printf '%s\n' "${IO_DEBUG_STDERR_ACTUAL}" >&2
  exit 1
fi

FS_OUT="$("${ROOT_DIR}/tools/ailang" run "${TMP_DIR}/fs.aos")"
FS_EXPECTED='true
true
fs-ok
false
true
false
Ok#ok1(type=int value=0)'

if [[ "${FS_OUT}" != "${FS_EXPECTED}" ]]; then
  echo "stdlib fs behavior mismatch" >&2
  echo "expected:" >&2
  printf '%s\n' "${FS_EXPECTED}" >&2
  echo "actual:" >&2
  printf '%s\n' "${FS_OUT}" >&2
  exit 1
fi

PROCESS_SYSTEM_OUT="$(
  AILANG_STDLIB_BEHAVIOR_ENV=stdlib-ok \
    "${ROOT_DIR}/tools/ailang" run "${TMP_DIR}/process-system.aos"
)"
PROCESS_SYSTEM_EXPECTED_PREFIX="${ROOT_DIR}
stdlib-ok"

if [[ "${PROCESS_SYSTEM_OUT}" != "${PROCESS_SYSTEM_EXPECTED_PREFIX}"$'\n'* ]]; then
  echo "stdlib process/system cwd/env behavior mismatch" >&2
  echo "expected prefix:" >&2
  printf '%s\n' "${PROCESS_SYSTEM_EXPECTED_PREFIX}" >&2
  echo "actual:" >&2
  printf '%s\n' "${PROCESS_SYSTEM_OUT}" >&2
  exit 1
fi

PROCESS_SYSTEM_LINE_COUNT="$(printf '%s\n' "${PROCESS_SYSTEM_OUT}" | wc -l | tr -d ' ')"
if [[ "${PROCESS_SYSTEM_LINE_COUNT}" != "6" ]]; then
  echo "stdlib process/system line count mismatch" >&2
  printf '%s\n' "${PROCESS_SYSTEM_OUT}" >&2
  exit 1
fi

TIME_OUT="$("${ROOT_DIR}/tools/ailang" run "${TMP_DIR}/time.aos")"
TIME_EXPECTED='12345
UTC
0
true
Ok#ok1(type=int value=0)'

if [[ "${TIME_OUT}" != "${TIME_EXPECTED}" ]]; then
  echo "stdlib time behavior mismatch" >&2
  echo "expected:" >&2
  printf '%s\n' "${TIME_EXPECTED}" >&2
  echo "actual:" >&2
  printf '%s\n' "${TIME_OUT}" >&2
  exit 1
fi

echo "stdlib behavior: PASS"
