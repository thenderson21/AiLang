# AiBC1

AiBC1 is the deterministic bytecode container for AiLang VM execution.

## Header

Root node must be `Bytecode#...` with required attrs:

- `magic="AIBC"` (container magic)
- `format="AiBC1"` (encoding family)
- `version=1` (schema version)
- `flags=0` (reserved byte; non-zero reserved for future use)

VM loader requirements:

- reject missing/invalid `magic` with deterministic `VM001`
- reject unsupported `format` with deterministic `VM001`
- reject unsupported `version` with deterministic `VM001`
- reject missing/invalid `flags` with deterministic `VM001`

## Sections

Children are ordered sections:

- `Const#...`
- `Func#...`

No section may rely on map/hash iteration order.

## Constant Pool

Each `Const` child represents one constant.

- required attrs:
  - `kind=string|int|bool|null|node`
  - `value=...`
- constants are addressed by zero-based child index
- compiler emits constants in deterministic first-seen order for the canonical walk

`kind=node` uses canonical AOS text encoding of exactly one node value.

## Function Table

Each `Func` child defines one callable unit.

- required attrs:
  - `name=<identifier>`
  - `params="<csv>"`
  - `locals="<csv>"`
- instruction stream is ordered `Inst` children
- function index is child order

## Instructions

Each `Inst` has required `op` and optional operands `a`, `b`, `s`.

- stack/data: `CONST`, `LOAD_LOCAL`, `STORE_LOCAL`, `POP`
- control flow: `JUMP`, `JUMP_IF_FALSE`, `RETURN`
- calls: `CALL`, `CALL_SYS`, `ASYNC_CALL`, `ASYNC_CALL_SYS`, `AWAIT`
- structured concurrency: `PAR_BEGIN`, `PAR_FORK`, `PAR_JOIN`, `PAR_CANCEL`
- primitive ops: `EQ`, `ADD_INT`, `STR_CONCAT`, `TO_STRING`, `STR_ESCAPE`
- node ops: `NODE_KIND`, `NODE_ID`, `ATTR_COUNT`, `ATTR_KEY`, `ATTR_VALUE_KIND`, `ATTR_VALUE_STRING`, `ATTR_VALUE_INT`, `ATTR_VALUE_BOOL`, `CHILD_COUNT`, `CHILD_AT`, `MAKE_BLOCK`, `APPEND_CHILD`, `MAKE_ERR`, `MAKE_LIT_STRING`, `MAKE_NODE`

## Binary Mapping

When serialized to raw bytes by backend tooling, numeric fields are little-endian.
Canonical byte streams must be deterministic for identical input programs.

## Error Model

VM failures must be deterministic `Err` nodes:

- `code=VM001`
- stable `message`
- stable `nodeId`

Unsupported constructs during emit/load/run must return `VM001`, never crash.
