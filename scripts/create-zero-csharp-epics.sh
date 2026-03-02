#!/usr/bin/env bash
set -euo pipefail

REPO="${1:-}"
if [[ -z "${REPO}" ]]; then
  REPO="$(gh repo view --json nameWithOwner -q .nameWithOwner)"
fi

"$(dirname "$0")/sync-zero-csharp-project-meta.sh" "${REPO}" >/dev/null || true

create_epic() {
  local title="$1"
  local milestone="$2"
  local labels="$3"
  local body="$4"

  if gh issue list -R "${REPO}" --state all --search "${title} in:title" --json title --jq '.[].title' | rg -Fxq "${title}"; then
    echo "exists: ${title}"
    return 0
  fi

  gh issue create \
    -R "${REPO}" \
    --title "${title}" \
    --milestone "${milestone}" \
    --label "${labels}" \
    --body "${body}" >/dev/null
  echo "created: ${title}"
}

create_epic \
  "EPIC-ZC1: Runtime Behavioral Parity Closure" \
  "M1 Parity 100" \
  "parity,ci-gate,spec-impact" \
  "Goal:
Reach 100% canonical parity for runtime behavior and deterministic diagnostics.

Scope:
- Golden corpus output and exit parity
- Entrypoint parity (run source, embedded bytecode, embedded bundle, serve)
- No known semantic drift against SPEC

DoD:
- Parity gate is 100%
- Deterministic error code/message/nodeId parity
- CI parity gate required

Required issue fields:
- Behavioral contract reference
- Determinism impact
- Parity case(s)
- Acceptance test IDs"

create_epic \
  "EPIC-ZC2: C Runtime as Sole Engine" \
  "M2 C-only runtime" \
  "zero-csharp,parity,ci-gate" \
  "Goal:
Remove remaining runtime bridge fallback behavior and run C runtime as sole execution engine.

Scope:
- Remove transitional runtime fallback semantics
- C runtime path for all required entrypoints
- Keep AST as debug-only

DoD:
- No runtime fallback for production flow
- Runtime entrypoints execute through C path
- Required runtime tests green"

create_epic \
  "EPIC-ZC3: Repo-wide C# Deletion" \
  "M3 Zero C# repo" \
  "zero-csharp,ci-gate" \
  "Goal:
Remove all C# code and managed-toolchain dependency from mainline repo workflows.

Scope:
- Remove tracked .cs/.csproj/.sln/.slnx from mainline
- Replace tooling/tests/CLI flows with non-C# equivalents
- Preserve archival branch for historical comparison only

DoD:
- Zero-C# gate passes
- CI/scripts no longer rely on managed C# build tooling
- Full suite still green"

create_epic \
  "EPIC-ZC4: Compiler Benchmarking + Regression Gates" \
  "M4 Memory + Benchmark done" \
  "bench,ci-gate,spec-impact" \
  "Goal:
Introduce frozen benchmark baselines and enforce regression thresholds.

Scope:
- Compiler/runtime benchmark suite
- Baseline capture and threshold policy
- CI benchmark gate integration

DoD:
- Benchmark gate green
- Threshold policy documented and enforced
- Regression failures block merge"

create_epic \
  "EPIC-ZC5: Sample Program Production Completion" \
  "M4 Memory + Benchmark done" \
  "samples,parity,ci-gate" \
  "Goal:
Finish all sample programs to production completion bar.

Scope:
- Functional smoke
- Determinism snapshot
- Perf sanity
- Memory leak check

DoD:
- All samples marked COMPLETE in manifest
- Sample gate green"

create_epic \
  "EPIC-ZC6: Memory Management + Leak Tooling" \
  "M4 Memory + Benchmark done" \
  "gc,memory-leak,ci-gate,spec-impact" \
  "Goal:
Add deterministic RC + cycle collection and leak profiling/testing.

Scope:
- Runtime RC invariants
- Deterministic cycle collector pass
- Leak profile and leak-check tools
- CI memory gate

DoD:
- Memory/GC gate green
- Leak suite integrated in CI
- Memory behavior documented"

echo "Zero-C# epics sync complete for ${REPO}"
