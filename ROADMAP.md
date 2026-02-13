# AiLang Runtime & Platform Roadmap
Authoritative Direction – Focused Execution Plan

⸻

## Vision

AiLang is a deterministic, AI-optimized development platform.

Goals:
	•	Minimal host layer (C# = bootloader + syscalls only)
	•	Majority of semantics implemented in AiLang
	•	Deterministic execution
	•	Cross-platform (macOS, Windows, Linux)
	•	Future WASM + JIT capable
	•	Competes conceptually with .NET — but AI-first and simpler

⸻

## Core Principle

C# must be:
	•	Bootloader
	•	Syscall surface
	•	Process boundary
	•	Platform abstraction

All orchestration, lifecycle, routing, HTTP logic, JSON, runtime dispatch, etc. must live in AiLang.

⸻

## Phase 1 – Runtime Stabilization (Current Phase)

Status: In Progress

Objectives:
	•	Unified runtime.start(args) for run + serve
	•	Lifecycle orchestration in AiLang
	•	Routing in AiLang
	•	JSON in AiLang
	•	HTTP parsing in AiLang
	•	C# reduced to syscall layer

Remaining Work:
	•	Remove any remaining lifecycle knowledge from C#
	•	Ensure all command handling lives in runtime.aos
	•	Stabilize syscall namespace: sys.*

Exit Criteria:
	•	C# contains zero lifecycle or routing logic
	•	C# contains zero HTTP orchestration logic
	•	All orchestration flows through runtime.aos

⸻

## Phase 2 – Structured HTTP Platform

Objective: Real Web API capability

Additions Required:

Transport:
	•	TLS support
	•	Proper HTTP/1.1 support (request body, persistent connections)
	•	Structured request/response objects

Language-Level Contract:

HttpRequest:
	•	method
	•	path
	•	query
	•	headers (Map)
	•	body

HttpResponse:
	•	status
	•	headers
	•	body

Runtime Changes:
	•	Event#Message(type=“http.request”, payload=HttpRequest)
	•	Command#Emit(type=“http.response”, payload=HttpResponse)

Exit Criteria:
	•	HTTP apps no longer manipulate raw text
	•	JSON response built via Map + compiler.toJson
	•	TLS supported via sys.tls_listen

⸻

## Phase 3 – Bytecode VM

Objective: Remove interpreter bottleneck

Current:
	•	Tree-walking interpreter

Needed:
	•	Deterministic bytecode format
	•	AOT compiler: AOS → AIBYTE
	•	Bytecode VM executor
	•	Publish bundles embed bytecode, not AST

Why:
	•	Performance
	•	Platform independence
	•	JIT-ready
	•	WASM-ready

Exit Criteria:
	•	aic publish produces bytecode
	•	Runtime executes bytecode instead of AST
	•	Interpreter retained only for dev mode

Architecture Clarification:

The bytecode VM is a deterministic microkernel.

- Bytecode execution must be a pure state transition system.
- All host interaction occurs only through explicit `sys.*` dispatch.
- No host IO, time, randomness, or network logic may exist in the VM core.
- Syscall handling must remain capability-gated.

This ensures deterministic replay, WASM portability, and future JIT/AOT safety.
⸻

## Phase 4 – GUI Platform (Vector-Based)

Objective: Cross-platform UI system

Architecture:

C# syscalls:
	•	sys.gfx_createWindow
	•	sys.gfx_beginFrame
	•	sys.gfx_drawRect
	•	sys.gfx_drawText
	•	sys.gfx_endFrame
	•	sys.gfx_pollEvent

AiLang:
	•	State → UiTree
	•	UiTree diff
	•	Render commands
	•	Event#UiMessage events

Approach:

Retained-mode vector scene graph.

Why:
	•	Deterministic
	•	Cross-platform
	•	AI-friendly declarative structure
	•	Future WASM compatible

Exit Criteria:
	•	Minimal window opens
	•	Button click event works
	•	Same UiTree works on macOS + Windows

⸻

## Phase 5 – WASM Backend

Objective: Run AiLang in the browser

Options:
	1.	Compile bytecode VM to WASM
	2.	Compile AiLang bytecode directly to WASM

Preferred Path:
	•	VM compiled to WASM
	•	Same bytecode format used everywhere

Result:
	•	Same AiLang app runs:
	•	CLI
	•	Web API
	•	GUI
	•	Browser

Exit Criteria:
	•	Hello World runs in browser
	•	Same runtime kernel used

⸻

## Phase 6 – JIT (Optional, Performance Phase)

Objective: Compete with high-performance runtimes

Approach:
	•	JIT from bytecode → native
	•	Platform-specific codegen
	•	Or use Cranelift/LLVM

This is not required until:
	•	VM is stable
	•	HTTP + GUI are stable

⸻

## Distribution Strategy

Short Term:
	•	Separate runtime binaries per platform
	•	macOS arm64
	•	macOS x64
	•	Windows x64
	•	Linux x64

Mid Term:
	•	Unified bytecode bundle format
	•	Runtime version embedded in binary

Long Term:
	•	Multi-platform runtime releases
	•	Versioned runtime + stable ABI

⸻

Non-Goals (For Now)
	•	No ORM
	•	No MVC framework
	•	No async/await complexity
	•	No dynamic reflection system
	•	No third-party framework layering

Focus on:
	•	Runtime correctness
	•	Determinism
	•	Performance
	•	AI-optimized semantics

⸻

## Syscall Capability Audit v1 (Execution Track)

Objective: Establish the minimal syscall capability surface required for AI-authored libraries to support CLI, server, and GUI applications.

Scope:
	•	Capability-layer primitives only (no standard library implementation in this track)
	•	No host-level lifecycle or business semantics
	•	No architectural bypass of deterministic VM/syscall boundaries

Required capability groups:
	•	console
	•	process
	•	file
	•	net
	•	time
	•	crypto (minimal)
	•	ui (window/frame/event)

Execution sequence:
	1. Spec and permission model update for capability-group syscall gating
	2. Validator + syscall contract updates for new primitive signatures
	3. Host adapter implementation per group with stable error contracts
	4. Golden and integration coverage for deterministic behavior under capability calls

Definition of done for this track:
	•	Capability surface is sufficient for library-level CLI runtime support
	•	Capability surface is sufficient for library-level TCP/HTTP/WebSocket support
	•	Capability surface is sufficient for basic desktop GUI + event loop support
	•	No stdlib/framework semantics moved into host layer

Related planning docs:
	•	`Docs/SyscallAudit.md`
	•	`Docs/SyscallRequiredSpec_v1.md`
	•	`Docs/SyscallCoverageSummary.md`

⸻

Immediate Next Milestone

Choose ONE and execute fully:

A) Structured HttpRequest/HttpResponse contract
B) Bytecode VM foundation
C) Vector GUI syscall surface

Do not split focus.

⸻

Strategic Positioning

AiLang should be:
	•	More deterministic than .NET
	•	Simpler than Node
	•	More uniform than Rust
	•	More AI-readable than all of them

The advantage is architectural clarity, not feature count.
