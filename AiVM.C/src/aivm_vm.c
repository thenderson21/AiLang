#include "aivm_vm.h"

void aivm_init(AivmVm* vm, const AivmProgram* program)
{
    if (vm == NULL) {
        return;
    }

    vm->program = program;
    vm->instruction_pointer = 0U;
}

void aivm_step(AivmVm* vm)
{
    const AivmInstruction* instruction;

    if (vm == NULL || vm->program == NULL || vm->program->instructions == NULL) {
        return;
    }

    if (vm->instruction_pointer >= vm->program->instruction_count) {
        return;
    }

    instruction = &vm->program->instructions[vm->instruction_pointer];

    switch (instruction->opcode) {
        case AIVM_OP_NOP:
            vm->instruction_pointer += 1U;
            break;

        case AIVM_OP_HALT:
            vm->instruction_pointer = vm->program->instruction_count;
            break;

        case AIVM_OP_STUB:
            vm->instruction_pointer += 1U;
            break;

        default:
            vm->instruction_pointer = vm->program->instruction_count;
            break;
    }
}

void aivm_run(AivmVm* vm)
{
    if (vm == NULL || vm->program == NULL) {
        return;
    }

    while (vm->instruction_pointer < vm->program->instruction_count) {
        aivm_step(vm);
    }
}
