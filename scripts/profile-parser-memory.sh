#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

SOURCE_PATH="${1:-src/compiler/format.aos}"
TMP_DIR="${ROOT_DIR}/.tmp/parser-memory-profile"
OUT_DIR="${ROOT_DIR}/.tmp/parser-memory-profile-out"
APP_PATH="${TMP_DIR}/app.aos"

rm -rf "${TMP_DIR}" "${OUT_DIR}"
mkdir -p "${TMP_DIR}" "${OUT_DIR}"

cat > "${APP_PATH}" <<'AOS'
Program#parser_mem_profile_p1 {
  Import#parser_mem_profile_i1(path="../../src/compiler/parser.aos")
  Export#parser_mem_profile_e1(name=start)

  Let#parser_mem_profile_l1(name=start) {
    Fn#parser_mem_profile_f1(params=args) {
      Block#parser_mem_profile_b1 {
        Let#parser_mem_profile_l2(name=sourcePath) { ChildAt#parser_mem_profile_ca1 { Var#parser_mem_profile_v1(name=args) Lit#parser_mem_profile_i1(value=0) } }
        Let#parser_mem_profile_l3(name=sourceText) {
          Call#parser_mem_profile_c1(target=sys.bytes.toUtf8String) {
            Call#parser_mem_profile_c2(target=sys.fs.file.read) {
              AttrValueString#parser_mem_profile_avs1 {
                Var#parser_mem_profile_v2(name=sourcePath)
                Lit#parser_mem_profile_i2(value=0)
              }
            }
          }
        }
        Let#parser_mem_profile_l4(name=parsed) { Call#parser_mem_profile_c3(target=parse.parseNode) { Var#parser_mem_profile_v3(name=sourceText) } }
        Call#parser_mem_profile_c4(target=sys.stdout.writeLine) { NodeKind#parser_mem_profile_nk1 { Var#parser_mem_profile_v4(name=parsed) } }
        Return#parser_mem_profile_r1 { Lit#parser_mem_profile_i3(value=0) }
      }
    }
  }
}
AOS

set +e
./tools/ailang debug run "${APP_PATH}" --out "${OUT_DIR}" -- "${SOURCE_PATH}"
STATUS=$?
set -e

echo "parser-memory-profile status=${STATUS}"
echo "parser-memory-profile source=${SOURCE_PATH}"
echo "parser-memory-profile out=${OUT_DIR}"

if [[ -f "${OUT_DIR}/diagnostics.toml" ]]; then
  rg -n "memory = |node_roots = |node_kind_counts = " "${OUT_DIR}/diagnostics.toml" || true
fi

exit 0
