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

Current status: partial (`sys.stdout.writeLine`, `console.print`, `io.*`).

Required minimal primitives:

- `sys.console.write(text:string) -> void`
- `sys.console.writeLine(text:string) -> void`
- `sys.console.readLine() -> string`
- `sys.console.readAllStdin() -> string`
- `sys.console.writeErrLine(text:string) -> void`

### 2. process

Current status: partial (`sys.process.exit`, `sys.platform`, `sys.arch`, `sys.os.version`, `sys.runtime`).

Required minimal primitives:

- `sys.process.exit(code:int) -> void`
- `sys.process.args() -> node` (canonical AOS list-like node payload)
- `sys.process.env.get(name:string) -> string`
- `sys.process.cwd() -> string`

Optional (deferred):

- `sys.process.spawn(cmd:string, args:node) -> int` (defer unless required by concrete library).

### 3. file

Current status: partial (`sys.fs.file.read`, `sys.fs.file.exists`).

Required minimal primitives:

- `sys.fs.file.read(path:string) -> bytes`
- `sys.fs.file.write(path:string, data:bytes) -> void`
- `sys.fs.file.exists(path:string) -> bool`
- `sys.fs.path.exists(path:string) -> bool`
- `sys.fs.dir.create(path:string) -> void`
- `sys.fs.dir.list(path:string) -> node` (AOS node list)
- `sys.fs.path.stat(path:string) -> node` (AOS node attrs for type/size/mtime)

### 4. net

Current status: partial (`sys.net.listen`, `sys.net.listen.tls`, `sys.net.accept`, `sys.net.write`, `sys.net.close`).

Required minimal primitives:

- `sys.net.tcp.listen(host:string, port:int) -> int`
- `sys.net.tcp.listenTls(host:string, port:int, certPath:string, keyPath:string) -> int`
- `sys.net.tcp.accept(listenerHandle:int) -> int`
- `sys.net.tcp.read(connectionHandle:int, maxBytes:int) -> bytes`
- `sys.net.tcp.write(connectionHandle:int, data:bytes) -> int` (bytes written)
- `sys.net.close(handle:int) -> void`
- `sys.net.udp.bind(host:string, port:int) -> int`
- `sys.net.udp.recv(handle:int, maxBytes:int) -> node` (payload + peer)
- `sys.net.udp.send(handle:int, host:string, port:int, data:bytes) -> int`

Notes:

- Keep HTTP/WebSocket protocol logic in AiLang libraries; provide only transport primitives.

### 5. time

Current status: missing.

Required minimal primitives:

- `sys.time.nowUnixMs() -> int`
- `sys.time.monotonicMs() -> int`
- `sys.time.sleepMs(ms:int) -> void`

Notes:

- Determinism must remain explicit: time access is effectful and capability-gated.

### 6. string helpers (deterministic)

Current status: partial (`sys.str.utf8ByteCount`).

Required minimal primitives:

- `sys.str.utf8ByteCount(text:string) -> int`
- `sys.str.substring(text:string, start:int, length:int) -> string`
- `sys.str.remove(text:string, start:int, length:int) -> string`

Notes:

- `start`/`length` are deterministic Unicode-scalar indexes (not bytes).
- Out-of-range inputs are clamped; operations must not throw.

### 7. crypto (minimal)

Current status: missing.

Required minimal primitives:

- `sys.crypto.randomBytes(count:int) -> string` (byte string encoding contract to be specified)
- `sys.crypto.sha1(text:string) -> string`
- `sys.crypto.sha256(text:string) -> string`
- `sys.crypto.hmacSha256(key:string, text:string) -> string`
- `sys.crypto.base64Encode(text:string) -> string`
- `sys.crypto.base64Decode(text:string) -> string`

Notes:

- This is minimal protocol support, not a full cryptography library.

### 8. ui (window + frame + event)

Current status: missing.

Required minimal primitives:

- `sys.ui.createWindow(title:string, width:int, height:int) -> int`
- `sys.ui.beginFrame(windowHandle:int) -> void`
- `sys.ui.drawRect(windowHandle:int, x:int, y:int, w:int, h:int, color:string) -> void`
- `sys.ui.drawText(windowHandle:int, x:int, y:int, text:string, color:string, size:int) -> void`
- `sys.ui.endFrame(windowHandle:int) -> void`
- `sys.ui.pollEvent(windowHandle:int) -> node` (AOS event node)
- `sys.ui.waitFrame(windowHandle:int) -> void` (host frame/tick pacing primitive)
- `sys.ui.getWindowSize(windowHandle:int) -> node` (AOS node with width/height)
- `sys.ui.present(windowHandle:int) -> void`
- `sys.ui.closeWindow(windowHandle:int) -> void`

Notes:

- Keep retained UI semantics and app event handling in AiLang code, not host.
- Prefer `sys.ui.waitFrame` over `sys.time.sleepMs` for UI frame pacing when available.

## Determinism and Host Constraints

All primitives above must follow these constraints:

- No hidden side effects.
- All effects explicit via `sys.*`.
- Stable error shaping (`Err` with stable code/message/nodeId path).
- No language/lifecycle semantics in host adapters.
- VM remains deterministic state transition engine; non-deterministic data (time/random/network) only enters via explicit capability calls.
