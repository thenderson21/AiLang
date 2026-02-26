#ifndef AIVM_PROGRAM_H
#define AIVM_PROGRAM_H

#include <stddef.h>

typedef enum {
    AIVM_OP_NOP = 0,
    AIVM_OP_HALT = 1,
    AIVM_OP_STUB = 2
} AivmOpcode;

typedef struct {
    AivmOpcode opcode;
} AivmInstruction;

typedef struct {
    const AivmInstruction* instructions;
    size_t instruction_count;
} AivmProgram;

#endif
