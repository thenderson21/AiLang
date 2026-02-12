#!/usr/bin/env bash

set -e

REPO="$(gh repo view --json nameWithOwner -q .nameWithOwner)"

TASK_DIR="./tasks"

if [ ! -d "$TASK_DIR" ]; then
  echo "No tasks directory found at $TASK_DIR"
  exit 1
fi

for file in "$TASK_DIR"/*.md; do
  [ -e "$file" ] || continue

  TITLE=$(head -n 1 "$file" | sed 's/^# //')
  BODY=$(sed '1d' "$file")

  # Check if issue already exists
  EXISTS=$(gh issue list \
    --repo "$REPO" \
    --search "$TITLE in:title" \
    --json title \
    -q ".[].title" | grep -Fx "$TITLE" || true)

  if [ -n "$EXISTS" ]; then
    echo "Skipping (already exists): $TITLE"
  else
    echo "Creating issue: $TITLE"
    gh issue create \
      --repo "$REPO" \
      --title "$TITLE" \
      --body "$BODY"
  fi
done

echo "Done."