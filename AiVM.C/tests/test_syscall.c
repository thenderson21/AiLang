#include "aivm_syscall.h"

static int expect(int condition)
{
    return condition ? 0 : 1;
}

static int handler_echo(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (arg_count == 1U && args != NULL) {
        *result = args[0];
        return AIVM_SYSCALL_OK;
    }

    *result = aivm_value_void();
    return AIVM_SYSCALL_ERR_INVALID;
}

int main(void)
{
    AivmValue result;
    AivmValue arg;
    AivmSyscallStatus status;
    static const AivmSyscallBinding bindings[] = {
        { "sys.echo", handler_echo }
    };

    status = aivm_syscall_invoke(NULL, "sys.echo", NULL, 0U, &result);
    if (expect(status == AIVM_SYSCALL_ERR_INVALID) != 0) {
        return 1;
    }

    status = aivm_syscall_dispatch(NULL, 0U, "sys.echo", NULL, 0U, &result);
    if (expect(status == AIVM_SYSCALL_ERR_INVALID) != 0) {
        return 1;
    }

    status = aivm_syscall_dispatch(bindings, 1U, "sys.missing", NULL, 0U, &result);
    if (expect(status == AIVM_SYSCALL_ERR_NOT_FOUND) != 0) {
        return 1;
    }

    arg = aivm_value_int(123);
    status = aivm_syscall_dispatch(bindings, 1U, "sys.echo", &arg, 1U, &result);
    if (expect(status == AIVM_SYSCALL_OK) != 0) {
        return 1;
    }
    if (expect(result.type == AIVM_VAL_INT) != 0) {
        return 1;
    }
    if (expect(result.int_value == 123) != 0) {
        return 1;
    }

    return 0;
}
