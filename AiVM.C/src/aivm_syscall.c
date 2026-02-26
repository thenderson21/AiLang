#include "aivm_syscall.h"

int aivm_syscall_invoke(
    AivmSyscallHandler handler,
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    if (result == NULL) {
        return -2;
    }

    if (handler == NULL || target == NULL) {
        result->type = AIVM_VAL_VOID;
        return -1;
    }

    return handler(target, args, arg_count, result);
}
