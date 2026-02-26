#ifndef AIVM_PROGRAM_H
#define AIVM_PROGRAM_H

#include <stddef.h>
#include <stdint.h>

typedef enum {
    AIVM_OP_NOP = 0,
    AIVM_OP_HALT = 1,
    AIVM_OP_STUB = 2,
    AIVM_OP_PUSH_INT = 3,
    AIVM_OP_POP = 4,
    AIVM_OP_STORE_LOCAL = 5,
    AIVM_OP_LOAD_LOCAL = 6,
    AIVM_OP_ADD_INT = 7,
    AIVM_OP_JUMP = 8,
    AIVM_OP_JUMP_IF_FALSE = 9,
    AIVM_OP_PUSH_BOOL = 10,
    AIVM_OP_CALL = 11,
    AIVM_OP_RET = 12,
    AIVM_OP_EQ_INT = 13,
    AIVM_OP_EQ = 14
} AivmOpcode;

typedef struct {
    AivmOpcode opcode;
    int64_t operand_int;
} AivmInstruction;

typedef struct {
    uint32_t section_type;
    uint32_t section_size;
    uint32_t section_offset;
} AivmProgramSection;

enum {
    AIVM_PROGRAM_MAX_SECTIONS = 32
};

typedef struct {
    const AivmInstruction* instructions;
    size_t instruction_count;
    uint32_t format_version;
    uint32_t format_flags;
    uint32_t section_count;
    AivmProgramSection sections[AIVM_PROGRAM_MAX_SECTIONS];
} AivmProgram;

typedef enum {
    AIVM_PROGRAM_OK = 0,
    AIVM_PROGRAM_ERR_NULL = 1,
    AIVM_PROGRAM_ERR_TRUNCATED = 2,
    AIVM_PROGRAM_ERR_BAD_MAGIC = 3,
    AIVM_PROGRAM_ERR_UNSUPPORTED = 4,
    AIVM_PROGRAM_ERR_SECTION_OOB = 5,
    AIVM_PROGRAM_ERR_SECTION_LIMIT = 6
} AivmProgramStatus;

typedef struct {
    AivmProgramStatus status;
    size_t error_offset;
} AivmProgramLoadResult;

void aivm_program_clear(AivmProgram* program);
void aivm_program_init(AivmProgram* program, const AivmInstruction* instructions, size_t instruction_count);
AivmProgramLoadResult aivm_program_load_aibc1(const uint8_t* bytes, size_t byte_count, AivmProgram* out_program);

#endif
