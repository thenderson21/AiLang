# AiLang C Runtime --- Buffer Overrun Hardening Guide

This document defines defensive programming rules for the C portions of
the AiLang runtime, VM, and host tooling.

The goal is eliminating memory corruption classes while preserving
AiLang's architectural constraints:

-   Deterministic behavior
-   Mechanical host runtime
-   Spec-governed semantics
-   Minimal trusted code

These rules apply to:

-   VM implementation
-   module loaders
-   AST/bytecode decoders
-   syscall marshaling
-   string handling
-   parser/tokenizer
-   formatting/logging code
-   host adapters

------------------------------------------------------------------------

# 1. Checked Integer Arithmetic

Many buffer overruns originate from integer overflow during size
calculations.

Never perform unchecked size math.

Required helpers:

bool size_add(size_t a, size_t b, size_t\* out); bool size_mul(size_t a,
size_t b, size_t\* out); bool size_sub(size_t a, size_t b, size_t\*
out);

Any calculation used for allocation, indexing, copying, slicing, or
resizing must be overflow checked.

Example logic:

size_t bytes; if (!size_mul(count, element_size, &bytes)) return
ERR_OVERFLOW;

------------------------------------------------------------------------

# 2. Explicit Buffer Structures

All buffers must track:

ptr len cap

Example structure:

typedef struct { uint8_t\* ptr; size_t len; size_t cap; } Buffer;

Required invariants:

len \<= cap ptr != NULL when cap \> 0

Operations must enforce:

Writes: needed \<= cap - len Reads: offset \<= len

------------------------------------------------------------------------

# 3. No Implicit C String Semantics

Do not rely on NUL-terminated strings for runtime logic.

Avoid:

strcpy strcat sprintf gets scanf

Prefer explicit-length APIs:

memcpy memmove snprintf

Strings in AiLang runtime should be treated as:

(ptr, length)

Embedded NUL bytes must be allowed unless explicitly disallowed by spec.

------------------------------------------------------------------------

# 4. Centralized Memory Helpers

Memory operations must be implemented through a small set of reviewed
primitives.

Recommended primitives:

buffer_init buffer_reserve buffer_resize buffer_append buffer_copy
buffer_slice buffer_clear

Direct pointer arithmetic outside these helpers should be avoided.

------------------------------------------------------------------------

# 5. Validation Before Use

Any external or serialized data must be fully validated before use.

Examples:

-   module files
-   AST blobs
-   bytecode
-   string tables
-   syscall payloads
-   network input

Validation must check:

-   section lengths
-   element counts
-   offsets
-   alignment
-   tag values
-   duplicate sections
-   overlapping ranges
-   recursion depth

Execution must never depend on partially validated data.

------------------------------------------------------------------------

# 6. Separate Validation Phase

Binary inputs must be processed in two stages:

1.  Validation pass
2.  Execution / decoding pass

Validation must prove:

-   all offsets valid
-   all sizes valid
-   all indexes valid
-   no overlaps
-   no integer overflow

After validation succeeds, execution code may assume inputs are well
formed.

------------------------------------------------------------------------

# 7. Hard Maximum Limits

Even valid data can be abusive if extremely large.

Define explicit limits such as:

MAX_MODULE_SIZE MAX_STRING_LENGTH MAX_NODE_COUNT MAX_ARGUMENT_COUNT
MAX_RECURSION_DEPTH MAX_SECTION_COUNT

These limits protect against memory exhaustion, pathological inputs, and
denial-of-service cases.

------------------------------------------------------------------------

# 8. Harden Debug and Logging Code

Diagnostic code frequently introduces memory bugs.

Examples include:

-   error formatting
-   debug dumps
-   tracing
-   REPL output

Formatting must use bounded functions such as:

snprintf vsnprintf

Never assume fixed buffer sizes.

------------------------------------------------------------------------

# 9. Toolchain Hardening

All builds should enable compiler defenses.

Example flags:

-fstack-protector-strong -D_FORTIFY_SOURCE=2 -fPIE -Wall -Wextra -Werror

Linker protections where supported:

RELRO NX ASLR

These mitigations help detect memory corruption early.

------------------------------------------------------------------------

# 10. Sanitizers in CI

Debug and CI builds should run with sanitizers enabled.

Recommended:

AddressSanitizer (ASan) UndefinedBehaviorSanitizer (UBSan)

Fuzz targets should include:

-   lexer
-   parser
-   AST decoder
-   module loader
-   formatter
-   syscall argument parsing

------------------------------------------------------------------------

# 11. No Silent Truncation

Silent truncation is not allowed.

Functions must either:

-   succeed fully
-   or fail deterministically

Example errors:

ERR_BUFFER_TOO_SMALL ERR_OVERFLOW

------------------------------------------------------------------------

# 12. Memory Lifetime Safety

Buffer safety alone is not sufficient.

The runtime must also avoid:

-   use-after-free
-   double free
-   dangling pointers
-   aliasing mistakes
-   returning pointers into temporary buffers

Ownership rules must be clearly defined.

------------------------------------------------------------------------

# 13. Avoid Duplicate Memory Logic

Do not reimplement memory manipulation logic repeatedly.

Reuse centralized helpers for:

-   copy
-   append
-   slice
-   resize
-   format

This reduces audit surface area.

------------------------------------------------------------------------

# 14. Safe Failure Paths

Error handling must not produce invalid state.

Rules:

-   no partially initialized objects escape
-   cleanup must be deterministic
-   objects must not be reused after failure

Optional debug mode:

-   poison freed buffers
-   zero invalid memory

------------------------------------------------------------------------

# 15. Security Boundary Tests

Security-focused tests must be added for edge conditions.

Required test categories:

-   off-by-one boundaries
-   max size inputs
-   integer overflow attempts
-   truncated payloads
-   overlapping offsets
-   negative-to-unsigned conversions
-   deep recursion
-   malformed binary sections

Since AiLang is deterministic, these should become permanent golden
tests.

------------------------------------------------------------------------

# Summary

Priority order:

1.  Checked arithmetic helpers
2.  Explicit buffer structures
3.  Full validation pass for binary inputs
4.  Sanitizer + fuzz testing
5.  Toolchain hardening
6.  Centralized memory primitives

Malformed input should always result in deterministic failure rather
than heuristic recovery.
