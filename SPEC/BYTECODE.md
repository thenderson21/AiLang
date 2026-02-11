# AiBC1

AiBC1 is the deterministic bytecode container for AiLang VM execution.

## Container

- Root node: `Bytecode#...`
- Required attrs:
  - `format="AiBC1"`
  - `version=1`
- Children are ordered sections:
  - `Const#...`
  - `Func#...`

No section may rely on hash-map or runtime iteration order.

## Constant Pool

Each `Const` child represents one pool item.

- Required attrs:
  - `kind=string|int|bool|null`
  - `value=...`
- Constants are addressed by zero-based index in child order.
- Encoding is deterministic first-seen order from compiler walk.

## Functions

Each `Func` child defines one callable unit.

- Required attrs:
  - `name=<identifier>`
  - `params="<csv>"`
  - `locals="<csv>"`
- Instruction stream is the ordered `Inst` children.

Function table index is function child order.

## Instructions

Each `Inst` has required `op` and optional `a`, `b`, `s`.

- `CONST a` push constant index `a`
- `LOAD_LOCAL a` push local slot `a`
- `STORE_LOCAL a` pop -> local slot `a`
- `POP` pop one value
- `EQ` pop2 compare, push bool
- `ADD_INT` pop2 int add, push int
- `STR_CONCAT` pop2 string concat, push string
- `JUMP a` set pc to absolute instruction index `a`
- `JUMP_IF_FALSE a` pop bool, jump if false
- `CALL a b` call function index `a` with `b` args
- `CALL_SYS a s` call syscall/host target `s` with `a` args
- `RETURN` return top of stack or `void` if empty

All operands are deterministic and interpreted as little-endian numeric values when serialized to raw bytes by future backends.

## Error Model

VM errors are deterministic `Err` nodes with:

- `code=VM001`
- `message=<stable text>`
- `nodeId=<function|node id>`

Unsupported constructs during emit or run must return `VM001`, never crash.
