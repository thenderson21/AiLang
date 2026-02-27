#include "aivm_c_api.h"

static int expect(int condition)
{
    return condition ? 0 : 1;
}

int main(void)
{
    AivmCResult result;
    static const AivmInstruction ok_instructions[] = {
        { .opcode = AIVM_OP_NOP, .operand_int = 0 },
        { .opcode = AIVM_OP_HALT, .operand_int = 0 }
    };
    static const AivmInstruction bad_opcode[] = {
        { .opcode = (AivmOpcode)99, .operand_int = 0 }
    };
    static const uint8_t bad_magic_program[16] = {
        'X', 'I', 'B', 'C',
        1, 0, 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0
    };
    static const uint8_t valid_aibc1_program[56] = {
        'A', 'I', 'B', 'C',
        1, 0, 0, 0,
        0, 0, 0, 0,
        1, 0, 0, 0,
        1, 0, 0, 0,   /* section type: instructions */
        28, 0, 0, 0,  /* section size */
        2, 0, 0, 0,   /* instruction_count */
        3, 0, 0, 0,   /* PUSH_INT */
        5, 0, 0, 0, 0, 0, 0, 0,
        1, 0, 0, 0,   /* HALT */
        0, 0, 0, 0, 0, 0, 0, 0
    };

    result = aivm_c_execute_instructions(ok_instructions, 2U);
    if (expect(result.ok == 1) != 0) {
        return 1;
    }
    if (expect(result.loaded == 1) != 0) {
        return 1;
    }
    if (expect(result.load_status == AIVM_PROGRAM_OK) != 0) {
        return 1;
    }
    if (expect(result.status == AIVM_VM_STATUS_HALTED) != 0) {
        return 1;
    }

    result = aivm_c_execute_instructions(bad_opcode, 1U);
    if (expect(result.ok == 0) != 0) {
        return 1;
    }
    if (expect(result.error == AIVM_VM_ERR_INVALID_OPCODE) != 0) {
        return 1;
    }

    result = aivm_c_execute_instructions(NULL, 0U);
    if (expect(result.ok == 1) != 0) {
        return 1;
    }
    if (expect(result.status == AIVM_VM_STATUS_HALTED) != 0) {
        return 1;
    }

    result = aivm_c_execute_instructions(NULL, 1U);
    if (expect(result.ok == 0) != 0) {
        return 1;
    }
    if (expect(result.error == AIVM_VM_ERR_INVALID_PROGRAM) != 0) {
        return 1;
    }

    result = aivm_c_execute_aibc1(valid_aibc1_program, 56U);
    if (expect(result.loaded == 1) != 0) {
        return 1;
    }
    if (expect(result.load_status == AIVM_PROGRAM_OK) != 0) {
        return 1;
    }
    if (expect(result.ok == 1) != 0) {
        return 1;
    }
    if (expect(result.status == AIVM_VM_STATUS_HALTED) != 0) {
        return 1;
    }

    result = aivm_c_execute_aibc1(bad_magic_program, 16U);
    if (expect(result.loaded == 0) != 0) {
        return 1;
    }
    if (expect(result.load_status == AIVM_PROGRAM_ERR_BAD_MAGIC) != 0) {
        return 1;
    }
    if (expect(result.ok == 0) != 0) {
        return 1;
    }
    if (expect(result.error == AIVM_VM_ERR_INVALID_PROGRAM) != 0) {
        return 1;
    }

    return 0;
}
