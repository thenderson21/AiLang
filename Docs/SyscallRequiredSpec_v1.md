# Syscall Required Spec v1 (Capability-Layer Only)

Status: planning-only, no implementation in this change.

This document defines the minimal syscall primitives required so higher-level libraries can be written in AiLang for CLI, server, and GUI workloads.

## Capability Model v1

Required capability groups:

1. `console`
2. `process`
3. `file`
4. `net`
5. `time`
6. `crypto`
7. `ui`

Permission model requirement:

- Introduce group-based permission checks for `sys.*` so calls can be gated by required capability group instead of a single global `sys` permission.

## Group Status and Required Primitives

### 1. console

Current status: partial (`sys.stdout_writeLine`, `console.print`, `io.*`).

Required minimal primitives:

- `sys.console_write(text:string) -> void`
- `sys.console_writeLine(text:string) -> void`
- `sys.console_readLine() -> string`
- `sys.console_readAllStdin() -> string`
- `sys.console_writeErrLine(text:string) -> void`

### 2. process

Current status: partial (`sys.proc_exit`, `sys.platform`, `sys.arch`, `sys.os_version`, `sys.runtime`).

Required minimal primitives:

- `sys.process_exit(code:int) -> void`
- `sys.process_argv() -> node` (canonical AOS list-like node payload)
- `sys.process_envGet(name:string) -> string`
- `sys.process_cwd() -> string`

Optional (deferred):

- `sys.process_spawn(cmd:string, args:node) -> int` (defer unless required by concrete library).

### 3. file

Current status: partial (`sys.fs_readFile`, `sys.fs_fileExists`).

Required minimal primitives:

- `sys.fs_readFile(path:string) -> string`
- `sys.fs_writeFile(path:string, text:string) -> void`
- `sys.fs_fileExists(path:string) -> bool`
- `sys.fs_pathExists(path:string) -> bool`
- `sys.fs_makeDir(path:string) -> void`
- `sys.fs_readDir(path:string) -> node` (AOS node list)
- `sys.fs_stat(path:string) -> node` (AOS node attrs for type/size/mtime)

### 4. net

Current status: partial (`sys.net_listen`, `sys.net_listen_tls`, `sys.net_accept`, `sys.net_readHeaders`, `sys.net_write`, `sys.net_close`).

Required minimal primitives:

- `sys.net_tcpListen(host:string, port:int) -> int`
- `sys.net_tcpListenTls(host:string, port:int, certPath:string, keyPath:string) -> int`
- `sys.net_tcpAccept(listenerHandle:int) -> int`
- `sys.net_tcpRead(connectionHandle:int, maxBytes:int) -> string`
- `sys.net_tcpWrite(connectionHandle:int, data:string) -> int` (bytes written)
- `sys.net_close(handle:int) -> void`
- `sys.net_udpBind(host:string, port:int) -> int`
- `sys.net_udpRecv(handle:int, maxBytes:int) -> node` (payload + peer)
- `sys.net_udpSend(handle:int, host:string, port:int, data:string) -> int`

Notes:

- Keep HTTP/WebSocket protocol logic in AiLang libraries; provide only transport primitives.
- Existing `sys.net_readHeaders` can remain temporarily but should not be the long-term generic net primitive.

### 5. time

Current status: missing.

Required minimal primitives:

- `sys.time_nowUnixMs() -> int`
- `sys.time_monotonicMs() -> int`
- `sys.time_sleepMs(ms:int) -> void`

Notes:

- Determinism must remain explicit: time access is effectful and capability-gated.

### 6. string helpers (deterministic)

Current status: partial (`sys.str_utf8ByteCount`).

Required minimal primitives:

- `sys.str_utf8ByteCount(text:string) -> int`
- `sys.str_substring(text:string, start:int, length:int) -> string`
- `sys.str_remove(text:string, start:int, length:int) -> string`

Notes:

- `start`/`length` are deterministic Unicode-scalar indexes (not bytes).
- Out-of-range inputs are clamped; operations must not throw.

### 7. crypto (minimal)

Current status: missing.

Required minimal primitives:

- `sys.crypto_randomBytes(count:int) -> string` (byte string encoding contract to be specified)
- `sys.crypto_sha1(text:string) -> string`
- `sys.crypto_sha256(text:string) -> string`
- `sys.crypto_hmacSha256(key:string, text:string) -> string`
- `sys.crypto_base64Encode(text:string) -> string`
- `sys.crypto_base64Decode(text:string) -> string`

Notes:

- This is minimal protocol support, not a full cryptography library.

### 8. ui (window + frame + event)

Current status: missing.

Required minimal primitives:

- `sys.ui_createWindow(title:string, width:int, height:int) -> int`
- `sys.ui_beginFrame(windowHandle:int) -> void`
- `sys.ui_drawRect(windowHandle:int, x:int, y:int, w:int, h:int, color:string) -> void`
- `sys.ui_drawText(windowHandle:int, x:int, y:int, text:string, color:string, size:int) -> void`
- `sys.ui_endFrame(windowHandle:int) -> void`
- `sys.ui_pollEvent(windowHandle:int) -> node` (AOS event node)
- `sys.ui_waitFrame(windowHandle:int) -> void` (host frame/tick pacing primitive)
- `sys.ui_getWindowSize(windowHandle:int) -> node` (AOS node with width/height)
- `sys.ui_present(windowHandle:int) -> void`
- `sys.ui_closeWindow(windowHandle:int) -> void`

Notes:

- Keep retained UI semantics and app event handling in AiLang code, not host.
- Prefer `sys.ui_waitFrame` over `sys.time_sleepMs` for UI frame pacing when available.

## Determinism and Host Constraints

All primitives above must follow these constraints:

- No hidden side effects.
- All effects explicit via `sys.*`.
- Stable error shaping (`Err` with stable code/message/nodeId path).
- No language/lifecycle semantics in host adapters.
- VM remains deterministic state transition engine; non-deterministic data (time/random/network) only enters via explicit capability calls.
