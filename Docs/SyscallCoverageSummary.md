# Syscall Coverage Summary

Audit date: 2026-02-13

## What Exists

Existing syscall surface supports parts of:

- CLI output/input via `console.print` and `io.*` plus `sys.stdout_writeLine`
- Process exit and host metadata (`sys.proc_exit`, `sys.platform`, `sys.arch`, `sys.os_version`, `sys.runtime`)
- File read/existence (`sys.fs_readFile`, `sys.fs_fileExists`)
- TCP/TLS listener basics and HTTP-style header/body read (`sys.net_*` subset)
- Simple outbound HTTP GET (`sys.http_get`)

## What Was Added as Planning Artifacts

Added docs:

- `/Users/toddhenderson/RiderProjects/AiLang/Docs/SyscallAudit.md`
- `/Users/toddhenderson/RiderProjects/AiLang/Docs/SyscallRequiredSpec_v1.md`
- `/Users/toddhenderson/RiderProjects/AiLang/Docs/SyscallCoverageSummary.md`

Roadmap update:

- `/Users/toddhenderson/RiderProjects/AiLang/ROADMAP.md` updated with a new syscall capability audit workstream and execution sequence.

## Capability Coverage Decision

### CLI apps

Status: partial now, full after planned primitives.

Missing blockers:

- Consistent console family in `sys.*`
- Process argv/env/cwd primitives
- File write + directory/stat/readDir primitives

### TCP server

Status: partial now, full after planned primitives.

Missing blockers:

- Generic `tcpRead` primitive (current host read path is HTTP-specific)
- Host bind control (`host` parameter)

### HTTP server (library level)

Status: partial now, full after planned primitives.

Missing blockers:

- Generic socket read/write and time primitives for robust request loops/timeouts

### WebSocket (library level)

Status: not yet.

Missing blockers:

- Generic socket read/write framing support
- Time primitives (timeouts/ping cadence)
- Minimal crypto primitives (hash/base64/random)

### Basic GUI window + rendering

Status: not yet.

Missing blockers:

- Entire `ui` syscall group

### Event loop driven UI

Status: not yet.

Missing blockers:

- `ui_pollEvent` + frame lifecycle primitives

## Overall Result

After defining and scheduling the v1 primitives, the capability surface is sufficient to build:

- CLI libraries in AiLang
- TCP/HTTP/WebSocket libraries in AiLang
- Basic desktop UI/event loop libraries in AiLang

This task intentionally performs planning and issue generation only. No syscall implementations are included.

