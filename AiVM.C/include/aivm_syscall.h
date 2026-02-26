#ifndef AIVM_SYSCALL_H
#define AIVM_SYSCALL_H

#include <stddef.h>

#include "aivm_types.h"

typedef int (*AivmSyscallHandler)(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result
);

int aivm_syscall_invoke(
    AivmSyscallHandler handler,
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result
);

#endif
