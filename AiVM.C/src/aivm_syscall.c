#include "aivm_syscall.h"

#include <string.h>

AivmSyscallStatus aivm_syscall_invoke(
    AivmSyscallHandler handler,
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }

    if (handler == NULL || target == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }

    return (AivmSyscallStatus)handler(target, args, arg_count, result);
}

AivmSyscallStatus aivm_syscall_dispatch(
    const AivmSyscallBinding* bindings,
    size_t binding_count,
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    size_t index;

    if (result == NULL) {
        return AIVM_SYSCALL_ERR_NULL_RESULT;
    }

    if (bindings == NULL || target == NULL) {
        result->type = AIVM_VAL_VOID;
        return AIVM_SYSCALL_ERR_INVALID;
    }

    for (index = 0U; index < binding_count; index += 1U) {
        if (bindings[index].target == NULL || bindings[index].handler == NULL) {
            continue;
        }

        if (strcmp(bindings[index].target, target) == 0) {
            return aivm_syscall_invoke(bindings[index].handler, target, args, arg_count, result);
        }
    }

    result->type = AIVM_VAL_VOID;
    return AIVM_SYSCALL_ERR_NOT_FOUND;
}
