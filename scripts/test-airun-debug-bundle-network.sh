#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

TMP_DIR="${ROOT_DIR}/.tmp/test-airun-debug-bundle-network"
rm -rf "${TMP_DIR}"
mkdir -p "${TMP_DIR}"

cat > "${TMP_DIR}/app.aos" <<'EOF'
Program#p1 {
  Export#e1(name=start)

  Let#l1(name=start) {
    Fn#f1(params=args) {
      Block#b1 {
        Let#l2(name=op) {
          Call#c1(target=sys.net.tcp.connectTlsStart) {
            Lit#s1(value="definitely.invalid")
            Lit#p1(value=443)
          }
        }
        Call#c2(target=sys.net.async.await) { Var#v1(name=op) }
        Return#r1 { Lit#n1(value=0) }
      }
    }
  }
}
EOF

OUT_DIR="${TMP_DIR}/bundle"
"${ROOT_DIR}/tools/airun" debug capture run "${TMP_DIR}/app.aos" --out "${OUT_DIR}" >/dev/null 2>&1 || true

if ! grep -q '^network = { ' "${OUT_DIR}/diagnostics.toml"; then
  echo "debug bundle regression: missing network summary in diagnostics.toml" >&2
  exit 1
fi

if ! grep -q 'host = "definitely.invalid"' "${OUT_DIR}/diagnostics.toml"; then
  echo "debug bundle regression: network summary missing failed host" >&2
  exit 1
fi

cat > "${TMP_DIR}/app_localhost.aos" <<'EOF'
Program#p1 {
  Export#e1(name=start)

  Let#l1(name=start) {
    Fn#f1(params=args) {
      Block#b1 {
        Let#l2(name=op) {
          Call#c1(target=sys.net.tcp.connectStart) {
            Lit#s1(value="localhost")
            Lit#p1(value=1)
          }
        }
        Call#c2(target=sys.net.async.await) { Var#v1(name=op) }
        Return#r1 { Lit#n1(value=0) }
      }
    }
  }
}
EOF

OUT_DIR_LOCAL="${TMP_DIR}/bundle_localhost"
"${ROOT_DIR}/tools/airun" debug capture run "${TMP_DIR}/app_localhost.aos" --out "${OUT_DIR_LOCAL}" >/dev/null 2>&1 || true

if ! grep -Eq 'resolved_ip = "(127\.0\.0\.1|::1)"' "${OUT_DIR_LOCAL}/diagnostics.toml"; then
  echo "debug bundle regression: network summary missing resolved localhost ip" >&2
  exit 1
fi
