#include "aivm_vm.h"

void aivm_reset_state(AivmVm* vm)
{
    if (vm == NULL) {
        return;
    }

    vm->instruction_pointer = 0U;
    vm->status = AIVM_VM_STATUS_READY;
    vm->error = AIVM_VM_ERR_NONE;
    vm->stack_count = 0U;
    vm->call_frame_count = 0U;
    vm->locals_count = 0U;
}

void aivm_init(AivmVm* vm, const AivmProgram* program)
{
    if (vm == NULL) {
        return;
    }

    vm->program = program;
    aivm_reset_state(vm);
}

void aivm_halt(AivmVm* vm)
{
    if (vm == NULL || vm->program == NULL) {
        return;
    }

    vm->instruction_pointer = vm->program->instruction_count;
    vm->status = AIVM_VM_STATUS_HALTED;
}

int aivm_stack_push(AivmVm* vm, AivmValue value)
{
    if (vm == NULL) {
        return 0;
    }

    if (vm->stack_count >= AIVM_VM_STACK_CAPACITY) {
        vm->error = AIVM_VM_ERR_STACK_OVERFLOW;
        vm->status = AIVM_VM_STATUS_ERROR;
        return 0;
    }

    vm->stack[vm->stack_count] = value;
    vm->stack_count += 1U;
    return 1;
}

int aivm_stack_pop(AivmVm* vm, AivmValue* out_value)
{
    if (vm == NULL || out_value == NULL) {
        return 0;
    }

    if (vm->stack_count == 0U) {
        vm->error = AIVM_VM_ERR_STACK_UNDERFLOW;
        vm->status = AIVM_VM_STATUS_ERROR;
        return 0;
    }

    vm->stack_count -= 1U;
    *out_value = vm->stack[vm->stack_count];
    return 1;
}

void aivm_step(AivmVm* vm)
{
    const AivmInstruction* instruction;

    if (vm == NULL || vm->program == NULL || vm->program->instructions == NULL) {
        return;
    }

    if (vm->status == AIVM_VM_STATUS_HALTED || vm->status == AIVM_VM_STATUS_ERROR) {
        return;
    }

    if (vm->instruction_pointer >= vm->program->instruction_count) {
        vm->status = AIVM_VM_STATUS_HALTED;
        return;
    }

    vm->status = AIVM_VM_STATUS_RUNNING;
    instruction = &vm->program->instructions[vm->instruction_pointer];

    switch (instruction->opcode) {
        case AIVM_OP_NOP:
            vm->instruction_pointer += 1U;
            break;

        case AIVM_OP_HALT:
            aivm_halt(vm);
            break;

        case AIVM_OP_STUB:
            vm->instruction_pointer += 1U;
            break;

        default:
            vm->error = AIVM_VM_ERR_INVALID_OPCODE;
            vm->status = AIVM_VM_STATUS_ERROR;
            vm->instruction_pointer = vm->program->instruction_count;
            break;
    }

    if (vm->status == AIVM_VM_STATUS_RUNNING &&
        vm->instruction_pointer >= vm->program->instruction_count) {
        vm->status = AIVM_VM_STATUS_HALTED;
    }
}

void aivm_run(AivmVm* vm)
{
    if (vm == NULL || vm->program == NULL) {
        return;
    }

    while (vm->instruction_pointer < vm->program->instruction_count &&
           vm->status != AIVM_VM_STATUS_ERROR &&
           vm->status != AIVM_VM_STATUS_HALTED) {
        aivm_step(vm);
    }
}

const char* aivm_vm_error_code(AivmVmError error)
{
    switch (error) {
        case AIVM_VM_ERR_NONE:
            return "AIVM000";
        case AIVM_VM_ERR_INVALID_OPCODE:
            return "AIVM001";
        case AIVM_VM_ERR_STACK_OVERFLOW:
            return "AIVM002";
        case AIVM_VM_ERR_STACK_UNDERFLOW:
            return "AIVM003";
        default:
            return "AIVM999";
    }
}

const char* aivm_vm_error_message(AivmVmError error)
{
    switch (error) {
        case AIVM_VM_ERR_NONE:
            return "No error.";
        case AIVM_VM_ERR_INVALID_OPCODE:
            return "Invalid opcode.";
        case AIVM_VM_ERR_STACK_OVERFLOW:
            return "Stack overflow.";
        case AIVM_VM_ERR_STACK_UNDERFLOW:
            return "Stack underflow.";
        default:
            return "Unknown VM error.";
    }
}
