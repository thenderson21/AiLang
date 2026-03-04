# WASM Remote Capability Channel (MVP)

## Purpose

Define the minimal host transport contract for wasm client-to-server effects without adding protocol/application semantics into AiVM.

## Rules

- VM remains deterministic state transition only.
- Remote effects are exposed only via `sys.remote.call`.
- Host channel is mechanical capability routing.
- No JSON payload model at VM boundary.

## Syscall Surface (MVP)

- Target: `sys.remote.call`
- Contract ID: `111`
- Args:
  - `cap` (`string`)
  - `op` (`string`)
  - `value` (`int`)
- Return:
  - `int`

## Capability Policy (MVP)

- Capability grants are provided by host environment variable: `AIVM_REMOTE_CAPS`.
- Value is comma-separated capability names.
- If `cap` is not granted, host returns deterministic target-unavailable behavior.

## Reference Host Operation (MVP)

- `cap.remote` + `echoInt` => returns input integer value unchanged.

This operation is intentionally mechanical and exists to verify transport/capability flow in deterministic tests.

## Error Behavior

- Unsupported/missing capability or operation returns host target-unavailable semantics.
- On wasm runtime this is surfaced as `RUN101` with deterministic text.

## Security Baseline

- See `SPEC/WASM_REMOTE_SECURITY.md`.
- MVP enforces capability grants, session-token authorization, and deterministic size guards in host adapters.

## Non-goals (MVP)

- No websocket framing yet.
- No streams/notifications/ping yet.
- No application protocol semantics in VM.

Future protocol framing (HELLO/WELCOME/CALL/RESULT/ERROR, optional stream/notify) is layered on top of this syscall surface.

## Current Bridge Runtime

- `aivm_remote_stdio_bridge` is provided as a process-boundary transport bridge using length-prefixed binary frames over stdio.
- This bridge uses the same deterministic session engine and frame codec as `sys.remote.call` host routing.
- WebSocket transport can be added as another transport backend without changing frame/session semantics.
