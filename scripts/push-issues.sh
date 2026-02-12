#!/usr/bin/env bash

set -e

REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner)

echo "Creating roadmap issues in $REPO"
echo ""

create_issue () {
  gh issue create \
    --repo "$REPO" \
    --title "$1" \
    --label "roadmap" \
    --body "$2"
}

# 1️⃣ Full HTTP Platform
create_issue "HTTP: Full Request/Response Parsing + Structured Response" \
"Goal:
Implement full HTTP/1.1 request parsing and structured HttpResponse node.

Scope:
- Parse headers
- Parse query string
- Parse request body (Content-Length)
- Add HttpRequest(method,path,headers,query,body)
- Add HttpResponse(status,headers,body)
- Update Command#Emit(type=\"http.response\") to accept structured node

Constraints:
- Deterministic behavior
- Single-threaded
- No async
- Must pass golden tests

Acceptance:
- GET + POST tests
- Header + body parsing verified
- No regressions"

# 2️⃣ HTTPS Support
create_issue "HTTP: Add HTTPS / TLS Support to serve" \
"Goal:
Support HTTPS in airun serve.

Scope:
- Add TLS syscalls
- Support --tls-cert and --tls-key flags
- Serve over HTTPS

Constraints:
- Self-signed cert OK for testing
- Deterministic startup errors

Acceptance:
- HTTPS integration test passes
- HTTP remains unaffected"

# 3️⃣ WASM Backend
create_issue "Backend: WebAssembly (WASM) Target" \
"Goal:
Add WASM backend for AiLang.

Scope:
- Emit WASM from bytecode
- WASI support
- Publish .wasm bundles

Constraints:
- Deterministic output
- No fallback to AST

Acceptance:
- Simple apps run under WASI
- Golden tests added"

# 4️⃣ AOT Compiler
create_issue "Runtime: Ahead-of-Time (AOT) Native Compilation" \
"Goal:
Compile AiBC1 bytecode to native machine code.

Scope:
- Native backend (Cranelift or similar)
- Integrate with publish
- Cross-platform support

Constraints:
- Deterministic builds
- No interpreter fallback in AOT mode

Acceptance:
- AOT binary runs golden tests
- Performance benchmark documented"

# 5️⃣ Optional JIT Mode
create_issue "Runtime: Optional JIT Execution Mode" \
"Goal:
Add JIT compilation for hot paths.

Scope:
- VM hotspot detection
- Inline caching
- Dynamic optimization

Constraints:
- Preserve deterministic output
- Must pass full golden suite

Acceptance:
- JIT mode flag works
- Performance improvement measurable"

# 6️⃣ GUI Vector Canvas API
create_issue "GUI: Vector Canvas API + Event Loop" \
"Goal:
Enable cross-platform GUI apps in AiLang.

Scope:
- Window creation syscall
- Vector drawing primitives
- Event loop
- Basic input events

Constraints:
- Cross-platform (Mac/Linux/Windows)
- No proprietary dependencies

Acceptance:
- Example GUI app renders
- Events work deterministically"

# 7️⃣ Project Templates
create_issue "Tooling: Rich Project Templates (CLI / HTTP / GUI / Lib)" \
"Goal:
Expand aic new with project templates.

Scope:
- CLI template
- HTTP template
- GUI template
- Library template
- Deterministic directory structure

Constraints:
- No external deps
- Must build and run immediately

Acceptance:
- Each template produces working app"

# 8️⃣ Deterministic VM Debugger
create_issue "Tooling: Deterministic VM Trace Debugger" \
"Goal:
Add structured VM debugger and trace tooling.

Scope:
- Step execution mode
- Trace filtering
- Structured trace output

Constraints:
- Must not alter execution semantics
- Deterministic trace output

Acceptance:
- New trace tests
- Debug mode documentation"

echo ""
echo "✅ All roadmap issues created."
