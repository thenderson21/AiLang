# Sample Completion Manifest

This file is the source of truth for EPIC-ZC5.

Completion rule per sample:

- Functional smoke: pass
- Determinism snapshot: pass
- Perf sanity: pass
- Memory leak check: pass

Status is `COMPLETE` only when all four are pass.

| Sample | Functional | Determinism | Perf | Memory | Status |
|---|---|---|---|---|---|
| `samples/cli-fetch` | pass | pass | pass | pass | COMPLETE |
| `samples/weather-api` | pass | pass | pass | pass | COMPLETE |
| `samples/weather-site` | pass | pass | pass | pass | COMPLETE |
| `samples/cli-http-parallel` | pass | pass | pass | pass | COMPLETE |
