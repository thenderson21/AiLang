#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GOLDEN_DIR="${ROOT_DIR}/examples/golden"
PUBLISH_DIR="${GOLDEN_DIR}/publish"
PUBLISHCASES_DIR="${GOLDEN_DIR}/publishcases"

mkdir -p "${PUBLISH_DIR}/binary_runs"
mkdir -p "${PUBLISH_DIR}/bundle_cycle_error"
mkdir -p "${PUBLISH_DIR}/bundle_single_file"
mkdir -p "${PUBLISH_DIR}/bundle_with_import"
mkdir -p "${PUBLISH_DIR}/missing_manifest"
mkdir -p "${PUBLISH_DIR}/overwrite_bundle"
mkdir -p "${PUBLISH_DIR}/success"
mkdir -p "${PUBLISH_DIR}/writes_bundle"

mkdir -p "${PUBLISHCASES_DIR}/include_missing_library"
mkdir -p "${PUBLISHCASES_DIR}/include_success/libs/aivectra"
mkdir -p "${PUBLISHCASES_DIR}/include_version_mismatch/libs/aivectra"

cat > "${PUBLISH_DIR}/binary_runs/main.aos" <<'EOF'
Program#br1 {
  Let#br2(name=main) {
    Fn#br3(params=args) {
      Block#br4 {
        Call#br5(target=io.write) { Lit#br6(value="binary-ok") }
        Return#br7 { Lit#br8(value=0) }
      }
    }
  }
  Export#br9(name=main)
}
EOF

cat > "${PUBLISH_DIR}/binary_runs/project.aiproj" <<'EOF'
Program#p1 {
  Project#proj1(name="binaryrun" entryFile="main.aos" entryExport="main")
}
EOF

cat > "${PUBLISH_DIR}/binary_runs/binaryrun.aibundle" <<'EOF'
Bytecode#bc1(flags=0 format="AiBC1" magic="AIBC" version=1) { Func#f_main(locals="argv" name=main params="argv") { Inst#i0(op=RETURN) } Func#f_main(locals="argv" name=main params="argv") { Inst#i1(op=RETURN) } }
EOF

cat > "${PUBLISH_DIR}/bundle_cycle_error/a_main.aos" <<'EOF'
Program#ca1 {
  Import#ca2(path="b_dep.aos")
  Let#ca3(name=main) { Lit#ca4(value="ok") }
  Export#ca5(name=main)
}
EOF

cat > "${PUBLISH_DIR}/bundle_cycle_error/b_dep.aos" <<'EOF'
Program#cb1 {
  Import#cb2(path="a_main.aos")
  Let#cb3(name=dep) { Lit#cb4(value=1) }
  Export#cb5(name=dep)
}
EOF

cat > "${PUBLISH_DIR}/bundle_cycle_error/project.aiproj" <<'EOF'
Program#p1 {
  Project#proj1(name="cycle" entryFile="a_main.aos" entryExport="main")
}
EOF

cat > "${PUBLISH_DIR}/bundle_single_file/a_main.aos" <<'EOF'
Program#m1 {
  Let#m2(name=main) { Lit#m3(value="ok") }
  Export#m4(name=main)
}
EOF

cat > "${PUBLISH_DIR}/bundle_single_file/project.aiproj" <<'EOF'
Program#p1 {
  Project#proj1(name="single" entryFile="a_main.aos" entryExport="main")
}
EOF

cat > "${PUBLISH_DIR}/bundle_single_file/single.aibundle" <<'EOF'
Bytecode#bc1(flags=0 format="AiBC1" magic="AIBC" version=1) { Const#k0(kind=string value="ok") Func#f_main(locals="argv,main" name=main params="argv") { Inst#i0(a=0 op=CONST) Inst#i1(a=1 op=STORE_LOCAL) Inst#i2(op=RETURN) } }
EOF

cat > "${PUBLISH_DIR}/bundle_with_import/a_main.aos" <<'EOF'
Program#wm1 {
  Import#wm2(path="z_dep.aos")
  Let#wm3(name=main) { Lit#wm4(value="ok") }
  Export#wm5(name=main)
}
EOF

cat > "${PUBLISH_DIR}/bundle_with_import/z_dep.aos" <<'EOF'
Program#wd1 {
  Let#wd2(name=dep) { Lit#wd3(value=1) }
  Export#wd4(name=dep)
}
EOF

cat > "${PUBLISH_DIR}/bundle_with_import/project.aiproj" <<'EOF'
Program#p1 {
  Project#proj1(name="with_import" entryFile="a_main.aos" entryExport="main")
}
EOF

cat > "${PUBLISH_DIR}/bundle_with_import/with_import.aibundle" <<'EOF'
Bytecode#bc1(flags=0 format="AiBC1" magic="AIBC" version=1) { Const#k0(kind=string value="ok") Func#f_main(locals="argv,main" name=main params="argv") { Inst#i0(a=0 op=CONST) Inst#i1(a=1 op=STORE_LOCAL) Inst#i2(op=RETURN) } }
EOF

cat > "${PUBLISH_DIR}/overwrite_bundle/main.aos" <<'EOF'
Program#o1 {
  Let#o2(name=main) { Lit#o3(value="ok") }
  Export#o4(name=main)
}
EOF

cat > "${PUBLISH_DIR}/overwrite_bundle/project.aiproj" <<'EOF'
Program#p1 {
  Project#proj1(name="overwrite" entryFile="main.aos" entryExport="main")
}
EOF

cat > "${PUBLISH_DIR}/overwrite_bundle/overwrite.aibundle" <<'EOF'
Bytecode#bc1(flags=0 format="AiBC1" magic="AIBC" version=1) { Const#k0(kind=string value="ok") Func#f_main(locals="argv,main" name=main params="argv") { Inst#i0(a=0 op=CONST) Inst#i1(a=1 op=STORE_LOCAL) Inst#i2(op=RETURN) } }
EOF

cat > "${PUBLISH_DIR}/success/project.aiproj" <<'EOF'
Program#p1 {
  Project#proj1(name="myapp" entryFile="src/main.aos" entryExport="main")
}
EOF

cat > "${PUBLISH_DIR}/writes_bundle/main.aos" <<'EOF'
Program#w1 {
  Let#w2(name=main) { Lit#w3(value="ok") }
  Export#w4(name=main)
}
EOF

cat > "${PUBLISH_DIR}/writes_bundle/project.aiproj" <<'EOF'
Program#p1 {
  Project#proj1(name="writes" entryFile="main.aos" entryExport="main")
}
EOF

cat > "${PUBLISH_DIR}/writes_bundle/writes.aibundle" <<'EOF'
Bytecode#bc1(flags=0 format="AiBC1" magic="AIBC" version=1) { Const#k0(kind=string value="ok") Func#f_main(locals="argv,main" name=main params="argv") { Inst#i0(a=0 op=CONST) Inst#i1(a=1 op=STORE_LOCAL) Inst#i2(op=RETURN) } }
EOF

cat > "${PUBLISHCASES_DIR}/include_missing_library/main.aos" <<'EOF'
Program#p1 {
  Let#l1(name=start) { Lit#i1(value=0) }
  Export#e1(name=start)
}
EOF

cat > "${PUBLISHCASES_DIR}/include_missing_library/project.aiproj" <<'EOF'
Program#p1 {
  Project#proj1(name="app_include_missing" entryFile="main.aos" entryExport="start") {
    Include#inc1(name="AiVectra" path="libs/aivectra" version="0.1.0")
  }
}
EOF

cat > "${PUBLISHCASES_DIR}/include_success/main.aos" <<'EOF'
Program#p1 {
  Let#l1(name=start) { Lit#i1(value=0) }
  Export#e1(name=start)
}
EOF

cat > "${PUBLISHCASES_DIR}/include_success/project.aiproj" <<'EOF'
Program#p1 {
  Project#proj1(name="app_include_ok" entryFile="main.aos" entryExport="start") {
    Include#inc1(name="AiVectra" path="libs/aivectra" version="0.1.0")
  }
}
EOF

cat > "${PUBLISHCASES_DIR}/include_success/libs/aivectra/AiVectra.ailib" <<'EOF'
Program#p1 {
  Project#proj1(name="AiVectra" entryFile="src/lib.aos" entryExport="library" version="0.1.0")
}
EOF

cat > "${PUBLISHCASES_DIR}/include_success/app_include_ok.aibundle" <<'EOF'
Bytecode#bc1(flags=0 format="AiBC1" magic="AIBC" version=1) { Const#k0(kind=int value=0) Func#f_main(locals="argv,start" name=main params="argv") { Inst#i0(a=0 op=CONST) Inst#i1(a=1 op=STORE_LOCAL) Inst#i2(op=RETURN) } }
EOF

cat > "${PUBLISHCASES_DIR}/include_version_mismatch/main.aos" <<'EOF'
Program#p1 {
  Let#l1(name=start) { Lit#i1(value=0) }
  Export#e1(name=start)
}
EOF

cat > "${PUBLISHCASES_DIR}/include_version_mismatch/project.aiproj" <<'EOF'
Program#p1 {
  Project#proj1(name="app_include_version_mismatch" entryFile="main.aos" entryExport="start") {
    Include#inc1(name="AiVectra" path="libs/aivectra" version="0.2.0")
  }
}
EOF

cat > "${PUBLISHCASES_DIR}/include_version_mismatch/libs/aivectra/AiVectra.ailib" <<'EOF'
Program#p1 {
  Project#proj1(name="AiVectra" entryFile="src/lib.aos" entryExport="library" version="0.1.0")
}
EOF
