#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP_DIR="${ROOT_DIR}/.tmp/ailang-test-command"
APP_DIR="${TMP_DIR}/app"

case "$(uname -s)" in
  Darwin) HOST_PLATFORM="osx" ;;
  Linux) HOST_PLATFORM="linux" ;;
  MINGW*|MSYS*|CYGWIN*) HOST_PLATFORM="windows" ;;
  *) HOST_PLATFORM="unknown" ;;
esac

case "$(uname -m)" in
  arm64|aarch64) HOST_ARCH="arm64" ;;
  x86_64|amd64) HOST_ARCH="x64" ;;
  *) HOST_ARCH="unknown" ;;
esac

AILANG_BIN="${AILANG_BIN:-}"
if [[ -z "${AILANG_BIN}" && -x "${ROOT_DIR}/.artifacts/ailang-${HOST_PLATFORM}-${HOST_ARCH}/ailang" ]]; then
  AILANG_BIN="${ROOT_DIR}/.artifacts/ailang-${HOST_PLATFORM}-${HOST_ARCH}/ailang"
fi
if [[ -z "${AILANG_BIN}" && -x "${ROOT_DIR}/tools/ailang" ]]; then
  AILANG_BIN="${ROOT_DIR}/tools/ailang"
fi
if [[ -z "${AILANG_BIN}" ]]; then
  echo "missing ailang launcher; run ./build.sh first" >&2
  exit 1
fi

rm -rf "${TMP_DIR}"
mkdir -p "${APP_DIR}/src" "${APP_DIR}/tests"

cat > "${APP_DIR}/project.aiproj" <<'AILANG_PROJECT'
Program#p1 {
  Project#proj1(name="test-command-smoke" entryFile="src/app.aos" entryExport="start" version="0.0.1")
}
AILANG_PROJECT

cat > "${APP_DIR}/src/app.aos" <<'AILANG_SOURCE'
Program#p1 {
  Export#e1(name=start)
  Let#l1(name=start) { Fn#f1(params=args) { Block#b1 { Return#r1 { Lit#i1(value=0) } } } }
}
AILANG_SOURCE

cat > "${APP_DIR}/tests/001_pass.aos" <<'AILANG_SOURCE'
Program#p1 {
  Export#e1(name=start)
  Let#l1(name=start) {
    Fn#f1(params=args) {
      Block#b1 {
        Call#c1(target=sys.stdout.writeLine) { Lit#s1(value="test one") }
        Return#r1 { Lit#i1(value=0) }
      }
    }
  }
}
AILANG_SOURCE

TEST_OUT="$("${AILANG_BIN}" test "${APP_DIR}")"
[[ "${TEST_OUT}" == *"test one"* ]]
[[ "${TEST_OUT}" == *"Ok#ok1(type=int value=1)"* ]]

NO_TESTS_DIR="${TMP_DIR}/no-tests"
mkdir -p "${NO_TESTS_DIR}/src"
cat > "${NO_TESTS_DIR}/project.aiproj" <<'AILANG_PROJECT'
Program#p1 {
  Project#proj1(name="no-tests" entryFile="src/app.aos" entryExport="start" version="0.0.1")
}
AILANG_PROJECT
cat > "${NO_TESTS_DIR}/src/app.aos" <<'AILANG_SOURCE'
Program#p1 {
  Export#e1(name=start)
  Let#l1(name=start) { Fn#f1(params=args) { Block#b1 { Return#r1 { Lit#i1(value=0) } } } }
}
AILANG_SOURCE

NO_TESTS_OUT="$("${AILANG_BIN}" test "${NO_TESTS_DIR}")"
[[ "${NO_TESTS_OUT}" == *"Ok#ok1(type=int value=0)"* ]]
