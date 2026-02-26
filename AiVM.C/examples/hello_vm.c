#include <stdio.h>

#include "aivm_program.h"
#include "aivm_vm.h"

int main(void)
{
    static const AivmInstruction instructions[] = {
        { AIVM_OP_NOP },
        { AIVM_OP_HALT }
    };
    static const AivmProgram program = {
        instructions,
        sizeof(instructions) / sizeof(instructions[0])
    };
    AivmVm vm;

    aivm_init(&vm, &program);
    aivm_run(&vm);

    printf("AiVM.C run complete. instruction_pointer=%zu\n", vm.instruction_pointer);
    return 0;
}
