#!/bin/sh
set -eu

ACTIVE_ROOT="${AILANG_AGENT_BUS_ACTIVE_ROOT:-/tmp/codex/agent-bus}"
PERSISTENT_ROOT="${AILANG_AGENT_BUS_PERSISTENT_ROOT:-${CODEX_HOME:-$HOME/.codex}/agent-bus}"

ensure_dirs() {
  root="$1"
  mkdir -p "$root/requests" "$root/responses" "$root/locks" "$root/archive"
}

copy_tree() {
  src="$1"
  dst="$2"
  ensure_dirs "$dst"
  for sub in requests responses locks archive; do
    mkdir -p "$dst/$sub"
    if [ -d "$src/$sub" ]; then
      find "$src/$sub" -type f | while IFS= read -r file; do
        rel=${file#"$src/"}
        mkdir -p "$(dirname "$dst/$rel")"
        cp -f "$file" "$dst/$rel"
      done
    fi
  done
}

latest_mtime() {
  root="$1"
  if [ ! -d "$root" ]; then
    echo 0
    return
  fi
  if find "$root" -type f -exec stat -f '%m' {} \; >/dev/null 2>&1; then
    find "$root" -type f -exec stat -f '%m' {} \; 2>/dev/null | sort -nr | awk 'NR==1{print $1; found=1} END{if(!found) print 0}'
  else
    find "$root" -type f -exec stat -c '%Y' {} \; 2>/dev/null | sort -nr | awk 'NR==1{print $1; found=1} END{if(!found) print 0}'
  fi
}

active_exists=0
persistent_exists=0
[ -d "$ACTIVE_ROOT" ] && active_exists=1
[ -d "$PERSISTENT_ROOT" ] && persistent_exists=1

if [ "$active_exists" -eq 0 ] && [ "$persistent_exists" -eq 0 ]; then
  ensure_dirs "$ACTIVE_ROOT"
  ensure_dirs "$PERSISTENT_ROOT"
  exit 0
fi

if [ "$active_exists" -eq 0 ] && [ "$persistent_exists" -eq 1 ]; then
  copy_tree "$PERSISTENT_ROOT" "$ACTIVE_ROOT"
  exit 0
fi

if [ "$active_exists" -eq 1 ] && [ "$persistent_exists" -eq 0 ]; then
  ensure_dirs "$PERSISTENT_ROOT"
  copy_tree "$ACTIVE_ROOT" "$PERSISTENT_ROOT"
  exit 0
fi

active_mtime=$(latest_mtime "$ACTIVE_ROOT")
persistent_mtime=$(latest_mtime "$PERSISTENT_ROOT")

if [ "$active_mtime" -ge "$persistent_mtime" ]; then
  copy_tree "$ACTIVE_ROOT" "$PERSISTENT_ROOT"
else
  copy_tree "$PERSISTENT_ROOT" "$ACTIVE_ROOT"
fi
