#!/usr/bin/env bash
set -euo pipefail

TMP_ROOT="${AILANG_PACKAGE_SMOKE_ROOT:-$(mktemp -d "${TMPDIR:-/tmp}/ailang-package-smoke.XXXXXX")}"
APP_DIR="${TMP_ROOT}/package-app"
TEMPLATE_DIR="${TMP_ROOT}/template-app"

cleanup() {
  if [[ -z "${AILANG_PACKAGE_SMOKE_KEEP:-}" ]]; then
    rm -rf "${TMP_ROOT}"
  fi
}
trap cleanup EXIT

require_tool() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "missing required tool: $1" >&2
    exit 1
  fi
}

require_tool ailang
require_tool git

rm -rf "${APP_DIR}" "${TEMPLATE_DIR}"
mkdir -p "${APP_DIR}/src" "${TEMPLATE_DIR}/src"

cat > "${APP_DIR}/project.aiproj" <<'AILANG_PROJECT'
Program#p1 {
  Project#proj1(name="package-smoke" entryFile="src/app.aos" entryExport="start" version="0.0.1-beta.1") {
    Include#dep_ailang(name="ailang")
    Include#dep_json(name="std-json")
  }
}
AILANG_PROJECT

cat > "${APP_DIR}/src/app.aos" <<'AILANG_SOURCE'
Program#p1 {
  Import#i1(package="ailang" path="src/std/core.aos")
  Import#i2(package="std-json" path="src/json.aos")
  Export#e1(name=start)

  Let#l1(name=start) {
    Fn#f1(params=args) {
      Block#b1 {
        Let#l2(name=value) {
          Call#c1(target=resultValueOr) {
            Call#c2(target=parse) { Lit#s1(value="\"package-smoke\"") }
            Lit#s2(value="fallback")
          }
        }
        Call#c3(target=sys.stdout.writeLine) { StrConcat#sc1 { Lit#s3(value="Package smoke: ") Var#v1(name=value) } }
        Return#r1 { Lit#i1(value=0) }
      }
    }
  }
}
AILANG_SOURCE

ailang package restore "${APP_DIR}"
ailang package list "${APP_DIR}" | grep -q 'std-json'
ailang build "${APP_DIR}"
ailang run "${APP_DIR}" | grep -q 'Package smoke: "package-smoke"'

cat > "${TEMPLATE_DIR}/project.aiproj" <<'AILANG_PROJECT'
Program#p1 {
  Project#proj1(name="template-smoke" entryFile="src/app.aos" entryExport="start" version="0.0.1-beta.1") {
    Include#dep_aivectra(name="aivectra")
  }
}
AILANG_PROJECT

cat > "${TEMPLATE_DIR}/src/app.aos" <<'AILANG_SOURCE'
Program#p1 {
  Export#e1(name=start)
  Let#l1(name=start) { Fn#f1(params=args) { Block#b1 { Return#r1 { Lit#i1(value=0) } } } }
}
AILANG_SOURCE

ailang package restore "${TEMPLATE_DIR}"
ailang package list "${TEMPLATE_DIR}" | grep -q 'aivectra'
ailang template list projects "${TEMPLATE_DIR}" | grep -q 'aivectra/hello-name'
ailang template list files "${TEMPLATE_DIR}" | grep -q 'aivectra/view-basic'

echo "package workflow smoke passed"
