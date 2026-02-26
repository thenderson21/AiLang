#include "aivm_program.h"
#include "aivm_vm.h"

static int expect(int condition)
{
    return condition ? 0 : 1;
}

static int test_push_store_load_pop(void)
{
    AivmVm vm;
    AivmValue out;
    static const AivmInstruction instructions[] = {
        { .opcode = AIVM_OP_PUSH_INT, .operand_int = 41 },
        { .opcode = AIVM_OP_STORE_LOCAL, .operand_int = 0 },
        { .opcode = AIVM_OP_LOAD_LOCAL, .operand_int = 0 },
        { .opcode = AIVM_OP_PUSH_INT, .operand_int = 1 },
        { .opcode = AIVM_OP_POP, .operand_int = 0 },
        { .opcode = AIVM_OP_HALT, .operand_int = 0 }
    };
    static const AivmProgram program = {
        .instructions = instructions,
        .instruction_count = 6U,
        .format_version = 0U,
        .format_flags = 0U,
        .section_count = 0U
    };

    aivm_init(&vm, &program);
    aivm_run(&vm);

    if (expect(vm.status == AIVM_VM_STATUS_HALTED) != 0) {
        return 1;
    }
    if (expect(vm.locals_count == 1U) != 0) {
        return 1;
    }
    if (expect(vm.locals[0].type == AIVM_VAL_INT) != 0) {
        return 1;
    }
    if (expect(vm.locals[0].int_value == 41) != 0) {
        return 1;
    }
    if (expect(vm.stack_count == 1U) != 0) {
        return 1;
    }
    if (expect(aivm_stack_pop(&vm, &out) == 1) != 0) {
        return 1;
    }
    if (expect(out.type == AIVM_VAL_INT) != 0) {
        return 1;
    }
    if (expect(out.int_value == 41) != 0) {
        return 1;
    }

    return 0;
}

static int test_load_local_missing_sets_error(void)
{
    AivmVm vm;
    static const AivmInstruction instructions[] = {
        { .opcode = AIVM_OP_LOAD_LOCAL, .operand_int = 0 }
    };
    static const AivmProgram program = {
        .instructions = instructions,
        .instruction_count = 1U,
        .format_version = 0U,
        .format_flags = 0U,
        .section_count = 0U
    };

    aivm_init(&vm, &program);
    aivm_step(&vm);

    if (expect(vm.status == AIVM_VM_STATUS_ERROR) != 0) {
        return 1;
    }
    if (expect(vm.error == AIVM_VM_ERR_LOCAL_OUT_OF_RANGE) != 0) {
        return 1;
    }

    return 0;
}

static int test_add_int(void)
{
    AivmVm vm;
    AivmValue out;
    static const AivmInstruction instructions[] = {
        { .opcode = AIVM_OP_PUSH_INT, .operand_int = 2 },
        { .opcode = AIVM_OP_PUSH_INT, .operand_int = 3 },
        { .opcode = AIVM_OP_ADD_INT, .operand_int = 0 },
        { .opcode = AIVM_OP_HALT, .operand_int = 0 }
    };
    static const AivmProgram program = {
        .instructions = instructions,
        .instruction_count = 4U,
        .format_version = 0U,
        .format_flags = 0U,
        .section_count = 0U
    };

    aivm_init(&vm, &program);
    aivm_run(&vm);

    if (expect(vm.status == AIVM_VM_STATUS_HALTED) != 0) {
        return 1;
    }
    if (expect(aivm_stack_pop(&vm, &out) == 1) != 0) {
        return 1;
    }
    if (expect(out.type == AIVM_VAL_INT) != 0) {
        return 1;
    }
    if (expect(out.int_value == 5) != 0) {
        return 1;
    }

    return 0;
}

static int test_add_int_type_mismatch_sets_error(void)
{
    AivmVm vm;
    AivmValue non_int;
    static const AivmInstruction instructions[] = {
        { .opcode = AIVM_OP_ADD_INT, .operand_int = 0 }
    };
    static const AivmProgram program = {
        .instructions = instructions,
        .instruction_count = 1U,
        .format_version = 0U,
        .format_flags = 0U,
        .section_count = 0U
    };

    aivm_init(&vm, &program);
    (void)aivm_stack_push(&vm, aivm_value_int(1));
    non_int = aivm_value_string("x");
    (void)aivm_stack_push(&vm, non_int);
    aivm_step(&vm);

    if (expect(vm.status == AIVM_VM_STATUS_ERROR) != 0) {
        return 1;
    }
    if (expect(vm.error == AIVM_VM_ERR_TYPE_MISMATCH) != 0) {
        return 1;
    }

    return 0;
}

static int test_jump_skips_instruction(void)
{
    AivmVm vm;
    AivmValue out;
    static const AivmInstruction instructions[] = {
        { .opcode = AIVM_OP_JUMP, .operand_int = 2 },
        { .opcode = AIVM_OP_PUSH_INT, .operand_int = 111 },
        { .opcode = AIVM_OP_PUSH_INT, .operand_int = 222 },
        { .opcode = AIVM_OP_HALT, .operand_int = 0 }
    };
    static const AivmProgram program = {
        .instructions = instructions,
        .instruction_count = 4U,
        .format_version = 0U,
        .format_flags = 0U,
        .section_count = 0U
    };

    aivm_init(&vm, &program);
    aivm_run(&vm);

    if (expect(vm.status == AIVM_VM_STATUS_HALTED) != 0) {
        return 1;
    }
    if (expect(vm.stack_count == 1U) != 0) {
        return 1;
    }
    if (expect(aivm_stack_pop(&vm, &out) == 1) != 0) {
        return 1;
    }
    if (expect(out.int_value == 222) != 0) {
        return 1;
    }
    return 0;
}

static int test_jump_if_false_takes_branch(void)
{
    AivmVm vm;
    AivmValue out;
    static const AivmInstruction instructions[] = {
        { .opcode = AIVM_OP_PUSH_BOOL, .operand_int = 0 },
        { .opcode = AIVM_OP_JUMP_IF_FALSE, .operand_int = 3 },
        { .opcode = AIVM_OP_PUSH_INT, .operand_int = 111 },
        { .opcode = AIVM_OP_PUSH_INT, .operand_int = 333 },
        { .opcode = AIVM_OP_HALT, .operand_int = 0 }
    };
    static const AivmProgram program = {
        .instructions = instructions,
        .instruction_count = 5U,
        .format_version = 0U,
        .format_flags = 0U,
        .section_count = 0U
    };

    aivm_init(&vm, &program);
    aivm_run(&vm);

    if (expect(vm.status == AIVM_VM_STATUS_HALTED) != 0) {
        return 1;
    }
    if (expect(vm.stack_count == 1U) != 0) {
        return 1;
    }
    if (expect(aivm_stack_pop(&vm, &out) == 1) != 0) {
        return 1;
    }
    if (expect(out.int_value == 333) != 0) {
        return 1;
    }
    return 0;
}

static int test_jump_if_false_type_mismatch_sets_error(void)
{
    AivmVm vm;
    static const AivmInstruction instructions[] = {
        { .opcode = AIVM_OP_PUSH_INT, .operand_int = 1 },
        { .opcode = AIVM_OP_JUMP_IF_FALSE, .operand_int = 0 }
    };
    static const AivmProgram program = {
        .instructions = instructions,
        .instruction_count = 2U,
        .format_version = 0U,
        .format_flags = 0U,
        .section_count = 0U
    };

    aivm_init(&vm, &program);
    aivm_run(&vm);
    if (expect(vm.status == AIVM_VM_STATUS_ERROR) != 0) {
        return 1;
    }
    if (expect(vm.error == AIVM_VM_ERR_TYPE_MISMATCH) != 0) {
        return 1;
    }
    return 0;
}

static int test_call_ret_roundtrip(void)
{
    AivmVm vm;
    AivmValue out;
    static const AivmInstruction instructions[] = {
        { .opcode = AIVM_OP_CALL, .operand_int = 2 },
        { .opcode = AIVM_OP_HALT, .operand_int = 0 },
        { .opcode = AIVM_OP_PUSH_INT, .operand_int = 7 },
        { .opcode = AIVM_OP_RET, .operand_int = 0 }
    };
    static const AivmProgram program = {
        .instructions = instructions,
        .instruction_count = 4U,
        .format_version = 0U,
        .format_flags = 0U,
        .section_count = 0U
    };

    aivm_init(&vm, &program);
    aivm_run(&vm);

    if (expect(vm.status == AIVM_VM_STATUS_HALTED) != 0) {
        return 1;
    }
    if (expect(vm.call_frame_count == 0U) != 0) {
        return 1;
    }
    if (expect(aivm_stack_pop(&vm, &out) == 1) != 0) {
        return 1;
    }
    if (expect(out.type == AIVM_VAL_INT) != 0) {
        return 1;
    }
    if (expect(out.int_value == 7) != 0) {
        return 1;
    }

    return 0;
}

static int test_ret_underflow_sets_error(void)
{
    AivmVm vm;
    static const AivmInstruction instructions[] = {
        { .opcode = AIVM_OP_RET, .operand_int = 0 }
    };
    static const AivmProgram program = {
        .instructions = instructions,
        .instruction_count = 1U,
        .format_version = 0U,
        .format_flags = 0U,
        .section_count = 0U
    };

    aivm_init(&vm, &program);
    aivm_step(&vm);

    if (expect(vm.status == AIVM_VM_STATUS_ERROR) != 0) {
        return 1;
    }
    if (expect(vm.error == AIVM_VM_ERR_FRAME_UNDERFLOW) != 0) {
        return 1;
    }

    return 0;
}

int main(void)
{
    if (test_push_store_load_pop() != 0) {
        return 1;
    }
    if (test_add_int() != 0) {
        return 1;
    }
    if (test_load_local_missing_sets_error() != 0) {
        return 1;
    }
    if (test_add_int_type_mismatch_sets_error() != 0) {
        return 1;
    }
    if (test_jump_skips_instruction() != 0) {
        return 1;
    }
    if (test_jump_if_false_takes_branch() != 0) {
        return 1;
    }
    if (test_jump_if_false_type_mismatch_sets_error() != 0) {
        return 1;
    }
    if (test_call_ret_roundtrip() != 0) {
        return 1;
    }
    if (test_ret_underflow_sets_error() != 0) {
        return 1;
    }

    return 0;
}
