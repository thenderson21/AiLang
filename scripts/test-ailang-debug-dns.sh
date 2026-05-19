#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

OUTPUT="$("${ROOT_DIR}/tools/ailang" debug dns localhost 443 2>&1)"
printf '%s\n' "${OUTPUT}"

if ! printf '%s\n' "${OUTPUT}" | grep -Eq '^Ok#ok1\(type=string value="(127\.0\.0\.1|::1)"\)$'; then
  echo "debug dns regression: localhost did not resolve through ailang debug dns" >&2
  exit 1
fi
