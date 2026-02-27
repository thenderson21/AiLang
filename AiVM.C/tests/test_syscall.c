#include <string.h>

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

static int handler_draw_rect(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    (void)args;
    if (arg_count == 6U) {
        *result = aivm_value_void();
        return AIVM_SYSCALL_OK;
    }
    *result = aivm_value_void();
    return AIVM_SYSCALL_ERR_INVALID;
}

static int handler_window_size(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    if (args != NULL && arg_count == 1U && args[0].type == AIVM_VAL_INT) {
        *result = aivm_value_node(1);
        return AIVM_SYSCALL_OK;
    }
    *result = aivm_value_void();
    return AIVM_SYSCALL_ERR_INVALID;
}

static int handler_window_size_wrong_type(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValue* result)
{
    (void)target;
    (void)args;
    (void)arg_count;
    *result = aivm_value_string("wrong");
    return AIVM_SYSCALL_OK;
}

int main(void)
{
    AivmValue result;
    AivmValue arg;
    AivmValue window_arg;
    AivmValue rect_args[6];
    AivmSyscallStatus status;
    static const AivmSyscallBinding bindings[] = {
        { "sys.echo", handler_echo }
    };
    static const AivmSyscallBinding ui_bindings[] = {
        { "sys.ui_drawRect", handler_draw_rect },
        { "sys.ui_getWindowSize", handler_window_size }
    };
    static const AivmSyscallBinding ui_bad_return_bindings[] = {
        { "sys.ui_getWindowSize", handler_window_size_wrong_type }
    };

    status = aivm_syscall_invoke(NULL, "sys.echo", NULL, 0U, &result);
    if (expect(status == AIVM_SYSCALL_ERR_INVALID) != 0) {
        return 1;
    }
    if (expect(strcmp(aivm_syscall_status_code(status), "AIVMS001") == 0) != 0) {
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

    rect_args[0] = aivm_value_int(0);
    rect_args[1] = aivm_value_int(0);
    rect_args[2] = aivm_value_int(10);
    rect_args[3] = aivm_value_int(20);
    rect_args[4] = aivm_value_int(1);
    rect_args[5] = aivm_value_string("#fff");
    status = aivm_syscall_dispatch_checked(ui_bindings, 2U, "sys.ui_drawRect", rect_args, 6U, &result);
    if (expect(status == AIVM_SYSCALL_OK) != 0) {
        return 1;
    }
    if (expect(result.type == AIVM_VAL_VOID) != 0) {
        return 1;
    }

    status = aivm_syscall_dispatch_checked(ui_bindings, 2U, "sys.ui_drawRect", rect_args, 5U, &result);
    if (expect(status == AIVM_SYSCALL_ERR_CONTRACT) != 0) {
        return 1;
    }

    window_arg = aivm_value_int(1);
    status = aivm_syscall_dispatch_checked(ui_bindings, 2U, "sys.ui_getWindowSize", &window_arg, 1U, &result);
    if (expect(status == AIVM_SYSCALL_OK) != 0) {
        return 1;
    }
    if (expect(result.type == AIVM_VAL_NODE) != 0) {
        return 1;
    }

    status = aivm_syscall_dispatch_checked(ui_bad_return_bindings, 1U, "sys.ui_getWindowSize", &window_arg, 1U, &result);
    if (expect(status == AIVM_SYSCALL_ERR_RETURN_TYPE) != 0) {
        return 1;
    }
    if (expect(strcmp(aivm_syscall_status_message(status), "Syscall return type violated contract.") == 0) != 0) {
        return 1;
    }
    if (expect(strcmp(aivm_syscall_status_code((AivmSyscallStatus)-999), "AIVMS999") == 0) != 0) {
        return 1;
    }

    return 0;
}
