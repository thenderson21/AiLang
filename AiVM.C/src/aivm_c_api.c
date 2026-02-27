#include "aivm_c_api.h"

#include "aivm_runtime.h"

static AivmCResult result_defaults(void)
{
    AivmCResult result;
    result.ok = 0;
    result.loaded = 0;
    result.status = AIVM_VM_STATUS_READY;
    result.error = AIVM_VM_ERR_NONE;
    result.load_status = AIVM_PROGRAM_ERR_NULL;
    result.load_error_offset = 0U;
    return result;
}

AivmCResult aivm_c_execute_instructions(const AivmInstruction* instructions, size_t instruction_count)
{
    AivmProgram program;
    AivmVm vm;
    AivmCResult result = result_defaults();

    aivm_program_init(&program, instructions, instruction_count);
    result.loaded = 1;
    result.load_status = AIVM_PROGRAM_OK;
    result.load_error_offset = 0U;

    result.ok = aivm_execute_program(&program, &vm);
    result.status = vm.status;
    result.error = vm.error;
    return result;
}

AivmCResult aivm_c_execute_aibc1(const uint8_t* bytes, size_t byte_count)
{
    AivmProgram program;
    AivmVm vm;
    AivmProgramLoadResult load_result;
    AivmCResult result = result_defaults();

    load_result = aivm_program_load_aibc1(bytes, byte_count, &program);
    result.load_status = load_result.status;
    result.load_error_offset = load_result.error_offset;

    if (load_result.status != AIVM_PROGRAM_OK) {
        result.status = AIVM_VM_STATUS_ERROR;
        result.error = AIVM_VM_ERR_INVALID_PROGRAM;
        return result;
    }

    result.loaded = 1;
    result.ok = aivm_execute_program(&program, &vm);
    result.status = vm.status;
    result.error = vm.error;
    return result;
}
