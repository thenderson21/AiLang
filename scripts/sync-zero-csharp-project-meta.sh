#!/usr/bin/env bash
set -euo pipefail

REPO="${1:-}"
if [[ -z "${REPO}" ]]; then
  REPO="$(gh repo view --json nameWithOwner -q .nameWithOwner)"
fi

labels=(
  "parity:Parity and behavior matching"
  "zero-csharp:Repo-wide C# removal work"
  "gc:Garbage collection and memory model"
  "memory-leak:Leak detection and profiling"
  "bench:Benchmark and perf gate work"
  "samples:Sample program completion work"
  "ci-gate:Required CI gate wiring"
  "spec-impact:SPEC behavior/documentation impact"
)

milestones=(
  "M1 Parity 100"
  "M2 C-only runtime"
  "M3 Zero C# repo"
  "M4 Memory + Benchmark done"
)

for entry in "${labels[@]}"; do
  name="${entry%%:*}"
  desc="${entry#*:}"
  if gh label list -R "${REPO}" --limit 200 --json name --jq '.[].name' | rg -Fxq "${name}"; then
    gh label edit "${name}" -R "${REPO}" --description "${desc}" >/dev/null
  else
    gh label create "${name}" -R "${REPO}" --description "${desc}" >/dev/null
  fi
done

for ms in "${milestones[@]}"; do
  if gh api -X GET "repos/${REPO}/milestones?state=all&per_page=100" --jq '.[].title' | rg -Fxq "${ms}"; then
    continue
  fi
  gh api -X POST "repos/${REPO}/milestones" -f title="${ms}" >/dev/null
done

echo "Synced zero-csharp labels and milestones for ${REPO}"

