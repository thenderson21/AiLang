#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PREFERRED_C_SOURCE_DIR="${ROOT_DIR}/src/AiVM.Core/native"
AIVM_C_SOURCE_DIR="${AIVM_C_SOURCE_DIR:-${PREFERRED_C_SOURCE_DIR}}"
BUILD_SUFFIX="native"
BUILD_DIR="${AIVM_C_BUILD_DIR:-${ROOT_DIR}/.tmp/aivm-c-build-${BUILD_SUFFIX}}"
PARITY_REPORT="${AIVM_PARITY_REPORT:-${ROOT_DIR}/.tmp/aivm-dualrun-manifest/report.txt}"
PARITY_MANIFEST="${AIVM_PARITY_MANIFEST:-${AIVM_C_SOURCE_DIR}/tests/parity_commands_ci.txt}"
SHARED_FLAG="-DAIVM_BUILD_SHARED=OFF"
if [[ "${AIVM_BUILD_SHARED:-0}" == "1" ]]; then
  SHARED_FLAG="-DAIVM_BUILD_SHARED=ON"
fi
PARITY_CLI="${BUILD_DIR}/aivm_parity_cli"

if [[ -f "${AIVM_C_SOURCE_DIR}/CMakePresets.json" ]]; then
  pushd "${AIVM_C_SOURCE_DIR}" >/dev/null
  cmake --preset aivm-native-unix --fresh "${SHARED_FLAG}"
  cmake --build --preset aivm-native-unix-build
  ctest --preset aivm-native-unix-test
  popd >/dev/null
else
  cmake -S "${AIVM_C_SOURCE_DIR}" -B "${BUILD_DIR}" "${SHARED_FLAG}"
  cmake --build "${BUILD_DIR}"
  ctest --test-dir "${BUILD_DIR}" --output-on-failure
fi

if [[ -x "${ROOT_DIR}/tools/airun" ]]; then
  AIRUN_HELP_TEXT="$("${ROOT_DIR}/tools/airun" 2>&1 || true)"
  AIRUN_HAS_BUILD=0
  AIRUN_HAS_CLEAN=0
  if printf "%s\n" "${AIRUN_HELP_TEXT}" | rg -q '^\s+build(\s|$)'; then
    AIRUN_HAS_BUILD=1
  fi
  if printf "%s\n" "${AIRUN_HELP_TEXT}" | rg -q '^\s+clean(\s|$)'; then
    AIRUN_HAS_CLEAN=1
  fi

  "${ROOT_DIR}/tools/airun" run "${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos" --vm=c >/dev/null
  TMP_NATIVE_PUBLISH_DIR="${ROOT_DIR}/.tmp/aivm-c-native-publish-smoke"
  rm -rf "${TMP_NATIVE_PUBLISH_DIR}"
  "${ROOT_DIR}/tools/airun" publish "${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos" --out "${TMP_NATIVE_PUBLISH_DIR}" >/dev/null
  if [[ ! -f "${TMP_NATIVE_PUBLISH_DIR}/app.aibc1" ]]; then
    echo "native publish smoke failed: app.aibc1 was not produced" >&2
    exit 1
  fi
  if [[ ! -x "${TMP_NATIVE_PUBLISH_DIR}/vm_c_execute_src_main_params" ]]; then
    echo "native publish smoke failed: app executable is missing" >&2
    exit 1
  fi
  if [[ -e "${TMP_NATIVE_PUBLISH_DIR}/run.sh" || -e "${TMP_NATIVE_PUBLISH_DIR}/run.ps1" || -e "${TMP_NATIVE_PUBLISH_DIR}/run.cmd" ]]; then
    echo "native publish smoke failed: launcher files should not be generated" >&2
    exit 1
  fi
  "${TMP_NATIVE_PUBLISH_DIR}/vm_c_execute_src_main_params" >/dev/null
  if [[ $? -ne 0 ]]; then
    echo "native publish smoke failed: direct app executable did not run" >&2
    exit 1
  fi

  case "$(uname -s)" in
    Darwin) _plat="osx" ;;
    Linux) _plat="linux" ;;
    *) _plat="" ;;
  esac
  case "$(uname -m)" in
    arm64|aarch64) _arch="arm64" ;;
    x86_64|amd64) _arch="x64" ;;
    *) _arch="" ;;
  esac
  if [[ -n "${_plat}" && -n "${_arch}" ]]; then
    HOST_RID="${_plat}-${_arch}"
    TMP_NATIVE_PROJECT_DIR="${ROOT_DIR}/.tmp/aivm-c-native-project-target"
    TMP_NATIVE_PROJECT_OUT="${ROOT_DIR}/.tmp/aivm-c-native-project-target-out"
    rm -rf "${TMP_NATIVE_PROJECT_DIR}" "${TMP_NATIVE_PROJECT_OUT}"
    mkdir -p "${TMP_NATIVE_PROJECT_DIR}"
    cat > "${TMP_NATIVE_PROJECT_DIR}/project.aiproj" <<EOF
Program#p1 {
  Project#proj1(name="projtarget" entryFile="main.aos" entryExport="main" publishTarget="${HOST_RID}")
}
EOF
    cat > "${TMP_NATIVE_PROJECT_DIR}/main.aos" <<'EOF'
Bytecode#bc1(magic="AIBC" format="AiBC1" version=2 flags=0) {
  Func#f1(name=main params="argv" locals="") {
    Inst#i1(op=HALT)
  }
}
EOF
    "${ROOT_DIR}/tools/airun" publish "${TMP_NATIVE_PROJECT_DIR}" --out "${TMP_NATIVE_PROJECT_OUT}" >/dev/null
    if [[ ! -x "${TMP_NATIVE_PROJECT_OUT}/projtarget" ]]; then
      echo "native publish target-from-manifest failed: projtarget executable missing" >&2
      exit 1
    fi
  fi

  TMP_NATIVE_CACHE_PROJECT="${ROOT_DIR}/.tmp/aivm-c-native-cache-smoke"
  TMP_NATIVE_CACHE_OUT_NO="${ROOT_DIR}/.tmp/aivm-c-native-cache-out-no"
  TMP_NATIVE_CACHE_OUT_YES="${ROOT_DIR}/.tmp/aivm-c-native-cache-out-yes"
  rm -rf "${TMP_NATIVE_CACHE_PROJECT}" "${TMP_NATIVE_CACHE_OUT_NO}" "${TMP_NATIVE_CACHE_OUT_YES}"
  mkdir -p "${TMP_NATIVE_CACHE_PROJECT}"
  cat > "${TMP_NATIVE_CACHE_PROJECT}/project.aiproj" <<'EOF'
Program#p1 {
  Project#proj1(name="cache_smoke" entryFile="app.aos" entryExport="start")
}
EOF
  cat > "${TMP_NATIVE_CACHE_PROJECT}/app.aos" <<'EOF'
Program#p1 {
  Export#e1(name=start)
  Let#l1(name=start) {
    Fn#f1(params=argv) {
      Block#b1 {
        Return#r1 { Lit#i1(value=0) }
      }
    }
  }
}
EOF
  if [[ "${AIRUN_HAS_BUILD}" == "1" && "${AIRUN_HAS_CLEAN}" == "1" ]]; then
    "${ROOT_DIR}/tools/airun" clean "${TMP_NATIVE_CACHE_PROJECT}" >/dev/null
    "${ROOT_DIR}/tools/airun" build --no-cache "${TMP_NATIVE_CACHE_PROJECT}" --out "${TMP_NATIVE_CACHE_OUT_NO}" >/dev/null
    if find "${TMP_NATIVE_CACHE_PROJECT}/.toolchain/cache/airun" -type f -name app.aibc1 2>/dev/null | grep -q .; then
      echo "native cache smoke failed: --no-cache build populated cache unexpectedly" >&2
      exit 1
    fi
    "${ROOT_DIR}/tools/airun" build "${TMP_NATIVE_CACHE_PROJECT}" --out "${TMP_NATIVE_CACHE_OUT_YES}" >/dev/null
    if ! find "${TMP_NATIVE_CACHE_PROJECT}/.toolchain/cache/airun" -type f -name app.aibc1 2>/dev/null | grep -q .; then
      echo "native cache smoke failed: cached build did not write cache artifact" >&2
      exit 1
    fi
    "${ROOT_DIR}/tools/airun" clean "${TMP_NATIVE_CACHE_PROJECT}" >/dev/null
    if find "${TMP_NATIVE_CACHE_PROJECT}/.toolchain/cache/airun" -type f -name app.aibc1 2>/dev/null | grep -q .; then
      echo "native cache smoke failed: clean did not remove cache artifacts" >&2
      exit 1
    fi
  else
    echo "Skipping native cache smoke: tools/airun build/clean commands unavailable." >&2
  fi

  TMP_NATIVE_CALL_FIXUP_REPRO="${ROOT_DIR}/.tmp/aivm-c-native-call-fixup-repro.aos"
  cat > "${TMP_NATIVE_CALL_FIXUP_REPRO}" <<'EOF'
Program#p1 {
  Export#e1(name=start)
  Let#l1(name=start) {
    Fn#f1(params=argv) {
      Block#b1 {
        Let#l2(name=v) {
          Call#c1(target=helper) { Lit#i1(value=1) }
        }
        Return#r1 { Var#v1(name=v) }
      }
    }
  }
  Let#l3(name=helper) {
    Fn#f2(params=x) {
      Block#b2 {
        Return#r2 { Add#a1 { Var#v2(name=x) Lit#i2(value=1) } }
      }
    }
  }
}
EOF
  TMP_NATIVE_CALL_FIXUP_OUT="$("${ROOT_DIR}/tools/airun" run "${TMP_NATIVE_CALL_FIXUP_REPRO}" 2>&1 || true)"
  if [[ "${TMP_NATIVE_CALL_FIXUP_OUT}" != *"Ok#ok1(type=int value=2)"* ]]; then
    echo "native call fixup smoke failed: expected value=2 from helper call" >&2
    printf '%s\n' "${TMP_NATIVE_CALL_FIXUP_OUT}" >&2
    exit 1
  fi

  TMP_NATIVE_TAILCALL_REPRO="${ROOT_DIR}/.tmp/aivm-c-native-tailcall-repro.aos"
  cat > "${TMP_NATIVE_TAILCALL_REPRO}" <<'EOF'
Program#p1 {
  Export#e1(name=start)
  Let#l1(name=start) {
    Fn#f1(params=argv) {
      Block#b1 {
        Return#r1 { Call#c1(target=loop) { Lit#i1(value=0) Lit#i2(value=6) Lit#i3(value=0) } }
      }
    }
  }
  Let#l2(name=loop) {
    Fn#f2(params=i,limit,acc) {
      Block#b2 {
        If#i4 {
          Eq#e2 { Var#v1(name=i) Var#v2(name=limit) }
          Block#b3 { Return#r2 { Var#v3(name=acc) } }
          Block#b4 { }
        }
        Return#r3 {
          Call#c2(target=loop) {
            Add#a1 { Var#v4(name=i) Lit#i5(value=1) }
            Var#v5(name=limit)
            Add#a2 { Var#v6(name=acc) Lit#i6(value=1) }
          }
        }
      }
    }
  }
}
EOF
  TMP_NATIVE_TAILCALL_OUT="$("${ROOT_DIR}/tools/airun" run "${TMP_NATIVE_TAILCALL_REPRO}" 2>&1 || true)"
  if [[ "${TMP_NATIVE_TAILCALL_OUT}" != *"Ok#ok1(type=int value=6)"* ]]; then
    echo "native tail-call smoke failed: expected value=6 from recursive loop" >&2
    printf '%s\n' "${TMP_NATIVE_TAILCALL_OUT}" >&2
    exit 1
  fi

  TMP_NATIVE_DEBUG_MEM_DIR="${ROOT_DIR}/.tmp/aivm-c-native-debug-mem"
  TMP_NATIVE_DEBUG_MEM_OUT="${ROOT_DIR}/.tmp/aivm-c-native-debug-mem-out"
  TMP_NATIVE_DEBUG_MEM_APP="${TMP_NATIVE_DEBUG_MEM_DIR}/memory_pressure.aos"
  rm -rf "${TMP_NATIVE_DEBUG_MEM_DIR}" "${TMP_NATIVE_DEBUG_MEM_OUT}"
  mkdir -p "${TMP_NATIVE_DEBUG_MEM_DIR}"
  {
    echo 'Bytecode#bc1(magic="AIBC" format="AiBC1" version=2 flags=0) {'
    echo '  Const#k0(kind=string value="n")'
    echo '  Func#f1(name=main params="argv" locals="") {'
    n=1
    inst_id=1
    while [[ $n -le 300 ]]; do
      echo "    Inst#c${inst_id}(op=CONST a=0)"
      inst_id=$((inst_id + 1))
      echo "    Inst#m${inst_id}(op=MAKE_BLOCK)"
      inst_id=$((inst_id + 1))
      n=$((n + 1))
    done
    echo "    Inst#h${inst_id}(op=HALT)"
    echo '  }'
    echo '}'
  } > "${TMP_NATIVE_DEBUG_MEM_APP}"
  if "${ROOT_DIR}/tools/airun" debug run "${TMP_NATIVE_DEBUG_MEM_APP}" --out "${TMP_NATIVE_DEBUG_MEM_OUT}" >/dev/null 2>&1; then
    echo "native debug memory smoke failed: expected memory-pressure failure" >&2
    exit 1
  fi
  if [[ ! -f "${TMP_NATIVE_DEBUG_MEM_OUT}/diagnostics.toml" || ! -f "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml" ]]; then
    echo "native debug memory smoke failed: expected debug artifacts missing" >&2
    exit 1
  fi
  if [[ ! -f "${TMP_NATIVE_DEBUG_MEM_OUT}/config.toml" ]]; then
    echo "native debug memory smoke failed: expected config.toml missing" >&2
    exit 1
  fi
  if ! grep -q "status = \"error\"" "${TMP_NATIVE_DEBUG_MEM_OUT}/config.toml"; then
    echo "native debug memory smoke failed: expected status=error in config.toml" >&2
    exit 1
  fi
  if ! grep -q "node_gc_interval_allocations = 64" "${TMP_NATIVE_DEBUG_MEM_OUT}/config.toml"; then
    echo "native debug memory smoke failed: unexpected gc interval policy value in config.toml" >&2
    exit 1
  fi
  if ! grep -q "node_gc_pressure_threshold_nodes = 192" "${TMP_NATIVE_DEBUG_MEM_OUT}/config.toml"; then
    echo "native debug memory smoke failed: unexpected gc pressure threshold value in config.toml" >&2
    exit 1
  fi
  if ! grep -q "vm_code=AIVM011" "${TMP_NATIVE_DEBUG_MEM_OUT}/diagnostics.toml"; then
    echo "native debug memory smoke failed: expected vm_code=AIVM011 in diagnostics.toml" >&2
    exit 1
  fi
  if ! grep -Eq "detail=(AIVMM005: )?node arena capacity exceeded\\." "${TMP_NATIVE_DEBUG_MEM_OUT}/diagnostics.toml"; then
    echo "native debug memory smoke failed: expected node arena capacity detail in diagnostics.toml" >&2
    exit 1
  fi
  if ! grep -q "stack_count" "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml"; then
    echo "native debug memory smoke failed: stack snapshot missing in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "locals_count" "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml"; then
    echo "native debug memory smoke failed: locals snapshot missing in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "node_gc_interval_allocations" "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml"; then
    echo "native debug memory smoke failed: gc interval policy missing in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "node_gc_interval_allocations = 64" "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml"; then
    echo "native debug memory smoke failed: unexpected gc interval policy value in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "node_gc_allocations_since_gc" "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml"; then
    echo "native debug memory smoke failed: gc allocation counter missing in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "node_gc_allocations_since_gc = 0" "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml"; then
    echo "native debug memory smoke failed: expected gc allocation counter reset in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "node_count = 256" "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml"; then
    echo "native debug memory smoke failed: expected node_count=256 in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "node_high_water = 256" "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml"; then
    echo "native debug memory smoke failed: expected node_high_water=256 in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "node_gc_pressure_threshold_nodes" "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml"; then
    echo "native debug memory smoke failed: gc pressure threshold missing in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "node_gc_pressure_threshold_nodes = 192" "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml"; then
    echo "native debug memory smoke failed: unexpected gc pressure threshold value in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "node_gc_allocations_since_gc" "${TMP_NATIVE_DEBUG_MEM_OUT}/diagnostics.toml"; then
    echo "native debug memory smoke failed: gc allocation counter missing in diagnostics.toml memory telemetry" >&2
    exit 1
  fi
  if ! grep -Eq "node_gc_compactions = [1-9][0-9]*" "${TMP_NATIVE_DEBUG_MEM_OUT}/diagnostics.toml"; then
    echo "native debug memory smoke failed: expected gc compaction activity in diagnostics.toml memory telemetry" >&2
    exit 1
  fi
  if ! grep -Eq "node_gc_attempts = [1-9][0-9]*" "${TMP_NATIVE_DEBUG_MEM_OUT}/diagnostics.toml"; then
    echo "native debug memory smoke failed: expected gc attempt activity in diagnostics.toml memory telemetry" >&2
    exit 1
  fi
  if ! grep -q "node_gc_reclaimed_nodes = 0" "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml"; then
    echo "native debug memory smoke failed: expected no reclaimed nodes in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "node_arena_pressure_count = 1" "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml"; then
    echo "native debug memory smoke failed: expected node_arena_pressure_count=1 in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -Eq "node_gc_attempts = [1-9][0-9]*" "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml"; then
    echo "native debug memory smoke failed: expected node_gc_attempts>0 in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "string_arena_pressure_count = 0" "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml"; then
    echo "native debug memory smoke failed: expected string_arena_pressure_count=0 in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "bytes_arena_pressure_count = 0" "${TMP_NATIVE_DEBUG_MEM_OUT}/state_snapshots.toml"; then
    echo "native debug memory smoke failed: expected bytes_arena_pressure_count=0 in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "node_count = 256" "${TMP_NATIVE_DEBUG_MEM_OUT}/diagnostics.toml"; then
    echo "native debug memory smoke failed: expected node_count=256 in diagnostics.toml memory telemetry" >&2
    exit 1
  fi
  if ! grep -q "node_high_water = 256" "${TMP_NATIVE_DEBUG_MEM_OUT}/diagnostics.toml"; then
    echo "native debug memory smoke failed: expected node_high_water=256 in diagnostics.toml memory telemetry" >&2
    exit 1
  fi
  if ! grep -q "node_gc_allocations_since_gc = 0" "${TMP_NATIVE_DEBUG_MEM_OUT}/diagnostics.toml"; then
    echo "native debug memory smoke failed: expected gc allocation counter reset in diagnostics.toml memory telemetry" >&2
    exit 1
  fi
  if ! grep -q "node_gc_reclaimed_nodes = 0" "${TMP_NATIVE_DEBUG_MEM_OUT}/diagnostics.toml"; then
    echo "native debug memory smoke failed: expected no reclaimed nodes in diagnostics.toml memory telemetry" >&2
    exit 1
  fi
  if ! grep -q "node_arena_pressure_count = 1" "${TMP_NATIVE_DEBUG_MEM_OUT}/diagnostics.toml"; then
    echo "native debug memory smoke failed: expected node_arena_pressure_count=1 in diagnostics.toml memory telemetry" >&2
    exit 1
  fi
  if ! grep -q "string_arena_pressure_count = 0" "${TMP_NATIVE_DEBUG_MEM_OUT}/diagnostics.toml"; then
    echo "native debug memory smoke failed: expected string_arena_pressure_count=0 in diagnostics.toml memory telemetry" >&2
    exit 1
  fi
  if ! grep -q "bytes_arena_pressure_count = 0" "${TMP_NATIVE_DEBUG_MEM_OUT}/diagnostics.toml"; then
    echo "native debug memory smoke failed: expected bytes_arena_pressure_count=0 in diagnostics.toml memory telemetry" >&2
    exit 1
  fi
  if ! grep -q "node_gc_interval_allocations = 64" "${TMP_NATIVE_DEBUG_MEM_OUT}/diagnostics.toml"; then
    echo "native debug memory smoke failed: unexpected gc interval policy value in diagnostics.toml memory telemetry" >&2
    exit 1
  fi
  if ! grep -q "node_gc_pressure_threshold_nodes = 192" "${TMP_NATIVE_DEBUG_MEM_OUT}/diagnostics.toml"; then
    echo "native debug memory smoke failed: unexpected gc pressure threshold value in diagnostics.toml memory telemetry" >&2
    exit 1
  fi

  TMP_NATIVE_DEBUG_OK_DIR="${ROOT_DIR}/.tmp/aivm-c-native-debug-ok"
  TMP_NATIVE_DEBUG_OK_OUT="${ROOT_DIR}/.tmp/aivm-c-native-debug-ok-out"
  TMP_NATIVE_DEBUG_OK_APP="${TMP_NATIVE_DEBUG_OK_DIR}/success_path.aos"
  rm -rf "${TMP_NATIVE_DEBUG_OK_DIR}" "${TMP_NATIVE_DEBUG_OK_OUT}"
  mkdir -p "${TMP_NATIVE_DEBUG_OK_DIR}"
  cat > "${TMP_NATIVE_DEBUG_OK_APP}" <<'EOF'
Bytecode#bc1(magic="AIBC" format="AiBC1" version=2 flags=0) {
  Func#f1(name=main params="argv" locals="") {
    Inst#i1(op=HALT)
  }
}
EOF
  if ! "${ROOT_DIR}/tools/airun" debug run "${TMP_NATIVE_DEBUG_OK_APP}" --out "${TMP_NATIVE_DEBUG_OK_OUT}" >/dev/null 2>&1; then
    echo "native debug success smoke failed: expected successful debug run" >&2
    exit 1
  fi
  if ! grep -q "status = \"ok\"" "${TMP_NATIVE_DEBUG_OK_OUT}/config.toml"; then
    echo "native debug success smoke failed: expected status=ok in config.toml" >&2
    exit 1
  fi
  if ! grep -q "vm_code=AIVM000" "${TMP_NATIVE_DEBUG_OK_OUT}/diagnostics.toml"; then
    echo "native debug success smoke failed: expected vm_code=AIVM000 in diagnostics.toml" >&2
    exit 1
  fi
  if ! grep -q "string_arena_pressure_count = 0" "${TMP_NATIVE_DEBUG_OK_OUT}/diagnostics.toml"; then
    echo "native debug success smoke failed: expected string_arena_pressure_count=0 in diagnostics.toml" >&2
    exit 1
  fi
  if ! grep -q "bytes_arena_pressure_count = 0" "${TMP_NATIVE_DEBUG_OK_OUT}/diagnostics.toml"; then
    echo "native debug success smoke failed: expected bytes_arena_pressure_count=0 in diagnostics.toml" >&2
    exit 1
  fi
  if ! grep -q "node_arena_pressure_count = 0" "${TMP_NATIVE_DEBUG_OK_OUT}/diagnostics.toml"; then
    echo "native debug success smoke failed: expected node_arena_pressure_count=0 in diagnostics.toml" >&2
    exit 1
  fi
  if ! grep -q "string_arena_pressure_count = 0" "${TMP_NATIVE_DEBUG_OK_OUT}/state_snapshots.toml"; then
    echo "native debug success smoke failed: expected string_arena_pressure_count=0 in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "bytes_arena_pressure_count = 0" "${TMP_NATIVE_DEBUG_OK_OUT}/state_snapshots.toml"; then
    echo "native debug success smoke failed: expected bytes_arena_pressure_count=0 in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "node_arena_pressure_count = 0" "${TMP_NATIVE_DEBUG_OK_OUT}/state_snapshots.toml"; then
    echo "native debug success smoke failed: expected node_arena_pressure_count=0 in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "node_gc_attempts = 0" "${TMP_NATIVE_DEBUG_OK_OUT}/state_snapshots.toml"; then
    echo "native debug success smoke failed: expected node_gc_attempts=0 in state_snapshots.toml" >&2
    exit 1
  fi
  if ! grep -q "node_gc_attempts = 0" "${TMP_NATIVE_DEBUG_OK_OUT}/diagnostics.toml"; then
    echo "native debug success smoke failed: expected node_gc_attempts=0 in diagnostics.toml" >&2
    exit 1
  fi
fi

mkdir -p "$(dirname "${PARITY_REPORT}")"
if [[ -x "${ROOT_DIR}/tools/airun" ]]; then
  TASK_EDGE_TMP_DIR="${ROOT_DIR}/.tmp/aivm-task-edge-parity"
  mkdir -p "${TASK_EDGE_TMP_DIR}"
  TASK_EDGE_TOTAL=0
  TASK_EDGE_PASSED=0
  TASK_EDGE_FAILED=0
  {
    echo "task edge parity checks"
    echo "name|status|actual_exit|expected_exit"
  } > "${PARITY_REPORT}"

  run_task_edge_case() {
    local name="$1"
    local input="$2"
    local expected_output="$3"
    local expected_exit="$4"
    local actual_output="${TASK_EDGE_TMP_DIR}/${name}.actual.out"
    local actual_exit=0
    local status="PASS"

    set +e
    "${ROOT_DIR}/tools/airun" run "${input}" --vm=c > "${actual_output}" 2>&1
    actual_exit=$?
    set -e

    if [[ "${actual_exit}" != "${expected_exit}" ]]; then
      status="FAIL"
    fi
    if [[ "${status}" == "PASS" && -n "${expected_output}" ]]; then
      if ! "${PARITY_CLI}" "${actual_output}" "${expected_output}" >/dev/null 2>&1; then
        status="FAIL"
      fi
    fi
    if [[ "${status}" == "PASS" && -z "${expected_output}" ]]; then
      if [[ -s "${actual_output}" ]]; then
        status="FAIL"
      fi
    fi

    TASK_EDGE_TOTAL=$((TASK_EDGE_TOTAL + 1))
    if [[ "${status}" == "PASS" ]]; then
      TASK_EDGE_PASSED=$((TASK_EDGE_PASSED + 1))
    else
      TASK_EDGE_FAILED=$((TASK_EDGE_FAILED + 1))
      echo "task edge parity failed: ${name}" >&2
      if [[ -f "${actual_output}" ]]; then
        cat "${actual_output}" >&2
      fi
    fi
    printf '%s|%s|%s|%s\n' "${name}" "${status}" "${actual_exit}" "${expected_exit}" >> "${PARITY_REPORT}"
  }

  run_task_edge_case \
    "await_edge_invalid" \
    "${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_await_edge_invalid.aos" \
    "${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_await_edge_invalid.out" \
    "3"
  run_task_edge_case \
    "par_join_edge_invalid" \
    "${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_par_join_edge_invalid.aos" \
    "${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_par_join_edge_invalid.out" \
    "3"
  run_task_edge_case \
    "par_cancel_edge_noop" \
    "${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_par_cancel_edge_noop.aos" \
    "" \
    "0"

  printf 'summary|passed=%s|total=%s|failed=%s\n' "${TASK_EDGE_PASSED}" "${TASK_EDGE_TOTAL}" "${TASK_EDGE_FAILED}" >> "${PARITY_REPORT}"
  if [[ "${TASK_EDGE_FAILED}" != "0" ]]; then
    exit 1
  fi
else
  {
    echo "task edge parity checks"
    echo "summary|passed=0|total=0|failed=0|skipped=tools/airun-missing"
  } > "${PARITY_REPORT}"
fi

if [[ "${AIVM_MEM_LEAK_GATE:-0}" == "1" ]]; then
  leak_iterations="${AIVM_LEAK_CHECK_ITERATIONS:-50}"
  leak_target="${AIVM_LEAK_CHECK_TARGET:-${ROOT_DIR}/src/AiVM.Core/native/tests/parity_cases/vm_c_execute_src_main_params.aos}"
  AIVM_LEAK_MAX_GROWTH_KB="${AIVM_LEAK_MAX_GROWTH_KB:-2048}" \
    "${ROOT_DIR}/scripts/aivm-mem-leak-check.sh" "${leak_target}" "${leak_iterations}" >/dev/null
fi

if [[ "${AIVM_PERF_SMOKE:-0}" == "1" ]]; then
  "${ROOT_DIR}/scripts/aivm-c-perf-smoke.sh" "${AIVM_PERF_RUNS:-10}"
fi
