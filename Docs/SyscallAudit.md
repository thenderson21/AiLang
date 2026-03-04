# Syscall Capability Audit

## Scope

Audit date: 2026-02-13

This audit covers syscall capability surface in:

- `/Users/toddhenderson/RiderProjects/AiLang/src/AiVM.Core/ISyscallHost.cs`
- `/Users/toddhenderson/RiderProjects/AiLang/src/AiVM.Core/VmSyscallDispatcher.cs`
- `/Users/toddhenderson/RiderProjects/AiLang/src/AiVM.Core/SyscallContracts.cs`
- `/Users/toddhenderson/RiderProjects/AiLang/src/AiVM.Core/DefaultSyscallHost.cs`
- `/Users/toddhenderson/RiderProjects/AiLang/src/AiVM.Core/VmCapabilityDispatcher.cs`
- `/Users/toddhenderson/RiderProjects/AiLang/src/AiLang.Core/AosValidator.cs`

## Current Capability Groups

Current permission groups observed in validator/dispatch:

- `console` (non-sys builtin call path)
- `io` (non-sys builtin call path)
- `math` (pure builtin)
- `compiler` (compiler/runtime helper builtins)
- `sys` (all `sys.*` calls as one permission gate)

Required groups from this audit request:

- `console`
- `process`
- `file`
- `net`
- `time`
- `crypto`
- `ui`

Gap: runtime currently gates all syscall primitives under single `sys` permission, not per-capability group.

## Defined Syscalls

Defined `sys.*` targets (dispatcher + contracts):

| Syscall | Args | Return | Observed behavior |
|---|---:|---|---|
| `sys.net.listen` | `(port:int)` | `int` | Start TCP listener on loopback; returns listener handle. |
| `sys.net.listen.tls` | `(port:int, certPath:string, keyPath:string)` | `int` | Start TLS listener on loopback; returns listener handle or `-1` on cert failure. |
| `sys.net.accept` | `(listenerHandle:int)` | `int` | Blocking accept; returns connection handle or `-1`. |
| `sys.net.write` | `(connectionHandle:int, data:bytes)` | `void` | Writes raw bytes to socket stream. |
| `sys.net.tcp.close` | `(handle:int)` | `void` | Closes listener/connection handle. |
| `sys.stdout.writeLine` | `(text:string)` | `void` | Writes line to stdout. |
| `sys.process.exit` | `(code:int)` | `void` | Raises process-exit exception boundary. |
| `sys.fs.file.read` | `(path:string)` | `bytes` | Reads full file bytes. |
| `sys.fs.file.exists` | `(path:string)` | `bool` | Returns file existence. |
| `sys.str.utf8ByteCount` | `(text:string)` | `int` | UTF-8 byte count utility. |
| `sys.platform` | `()` | `string` | Host OS family (`macos`, `windows`, `linux`, `unknown`). |
| `sys.arch` | `()` | `string` | Host OS architecture. |
| `sys.os.version` | `()` | `string` | Host OS version string. |
| `sys.runtime` | `()` | `string` | Host runtime id (`airun-dotnet`). |

Related non-`sys` native calls exposed through VM capability dispatcher:

- `console.print`
- `io.print`
- `io.write`
- `io.readLine`
- `io.readAllStdin`
- `io.readFile`
- `io.fileExists`
- `io.pathExists`
- `io.makeDir`
- `io.writeFile`

## Observed Gaps

Relative to required minimal capability model:

- `console`: partial.
- `process`: partial.
- `file`: partial.
- `net`: partial.
- `time`: missing.
- `crypto`: missing.
- `ui`: missing.

Concrete gaps:

- No per-group syscall capability gating (`sys` is a single permission).
- No UDP primitives.
- No WebSocket support primitives (can be library-level, but missing required low-level socket reads and timeouts).
- No time primitives (`now`, monotonic clock, sleep, timers).
- No crypto primitives (hash/HMAC/random/base64 helpers needed by protocol libraries).
- No UI primitives for window/frame/event loop.
- File syscalls are read-oriented only; no `sys.fs.file.write`, `sys.fs.mkdir`, `sys.fs.dir.list`, `sys.fs.path.stat`.
- Process capability only exposes exit; missing argv/env/cwd/spawn surface.

## Notes on Host Coupling

- `DefaultSyscallHost` directly uses .NET APIs (`Console`, `File`, `HttpClient`, `TcpListener`, `TcpClient`, TLS classes).
- Network listener binding is loopback-only in current implementation, which limits server deployment behavior.
- Socket operations are currently synchronous/blocking host calls.
- HTTP parsing behavior is partially embedded in host (`NetReadHeaders` reads headers/body framing), coupling protocol details to host boundary.
- VM call path remains explicit (`CALL_SYS`), and syscall arity/type checks are centralized in `SyscallContracts`.
