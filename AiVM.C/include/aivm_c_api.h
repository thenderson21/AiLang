#ifndef AIVM_C_API_H
#define AIVM_C_API_H

#include <stddef.h>
#include <stdint.h>

#include "aivm_program.h"
#include "aivm_syscall.h"
#include "aivm_vm.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    int ok;
    int loaded;
    AivmVmStatus status;
    AivmVmError error;
    AivmProgramStatus load_status;
    size_t load_error_offset;
} AivmCResult;

AivmCResult aivm_c_execute_instructions(const AivmInstruction* instructions, size_t instruction_count);
AivmCResult aivm_c_execute_instructions_with_syscalls(
    const AivmInstruction* instructions,
    size_t instruction_count,
    const AivmSyscallBinding* bindings,
    size_t binding_count);
AivmCResult aivm_c_execute_program_with_syscalls(
    const AivmProgram* program,
    const AivmSyscallBinding* bindings,
    size_t binding_count);
AivmCResult aivm_c_execute_aibc1(const uint8_t* bytes, size_t byte_count);

#ifdef __cplusplus
}
#endif

#endif
