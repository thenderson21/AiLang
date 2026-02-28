#include "aivm_syscall_contracts.h"

static int expect(int condition)
{
    return condition ? 0 : 1;
}

int main(void)
{
    AivmValueType return_type;
    const AivmSyscallContract* contract;
    AivmValue draw_rect_args[6];
    AivmValue draw_text_args[6];
    AivmValue draw_line_args[7];
    AivmValue ui_window_args[3];
    AivmValue ui_window_id_arg[1];
    AivmValue console_write_arg[1];
    AivmValue str_args[3];
    AivmValue utf8_count_args[1];

    draw_rect_args[0] = aivm_value_int(0);
    draw_rect_args[1] = aivm_value_int(0);
    draw_rect_args[2] = aivm_value_int(100);
    draw_rect_args[3] = aivm_value_int(50);
    draw_rect_args[4] = aivm_value_int(1);
    draw_rect_args[5] = aivm_value_string("#fff");

    if (expect(aivm_syscall_contract_validate("sys.ui_drawRect", draw_rect_args, 6U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_value_equals(aivm_value_string(aivm_contract_status_code(AIVM_CONTRACT_OK)), aivm_value_string("AIVMC000")) == 1) != 0) {
        return 1;
    }
    if (expect(return_type == AIVM_VAL_VOID) != 0) {
        return 1;
    }
    contract = aivm_syscall_contract_find_by_target("sys.ui_drawRect");
    if (expect(contract != NULL) != 0) {
        return 1;
    }
    if (expect(contract->id == 48U) != 0) {
        return 1;
    }

    console_write_arg[0] = aivm_value_string("hello");
    if (expect(aivm_syscall_contract_validate("sys.console_write", console_write_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(6U, console_write_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.console_writeLine", console_write_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(7U, console_write_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.console_writeErrLine", console_write_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(10U, console_write_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.stdout_writeLine", console_write_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(16U, console_write_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(return_type == AIVM_VAL_VOID) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.console_readLine", NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(8U, NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.console_readAllStdin", NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(9U, NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(return_type == AIVM_VAL_STRING) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.process_cwd", NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(11U, NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.process_envGet", console_write_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(12U, console_write_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.platform", NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(28U, NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.arch", NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(29U, NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.os_version", NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(30U, NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.runtime", NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(31U, NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.process_argv", NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(18U, NULL, 0U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(return_type == AIVM_VAL_NODE) != 0) {
        return 1;
    }

    ui_window_args[0] = aivm_value_string("Hello");
    ui_window_args[1] = aivm_value_int(800);
    ui_window_args[2] = aivm_value_int(600);
    if (expect(aivm_syscall_contract_validate("sys.ui_createWindow", ui_window_args, 3U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(46U, ui_window_args, 3U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(return_type == AIVM_VAL_INT) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.ui_createWindow", ui_window_args, 2U, &return_type) == AIVM_CONTRACT_ERR_ARG_COUNT) != 0) {
        return 1;
    }

    ui_window_id_arg[0] = aivm_value_int(123);
    if (expect(aivm_syscall_contract_validate("sys.ui_beginFrame", ui_window_id_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(47U, ui_window_id_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.ui_waitFrame", ui_window_id_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(72U, ui_window_id_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.ui_endFrame", ui_window_id_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(50U, ui_window_id_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.ui_present", ui_window_id_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(52U, ui_window_id_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.ui_closeWindow", ui_window_id_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(53U, ui_window_id_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }

    if (expect(aivm_syscall_contract_validate("sys.ui_pollEvent", ui_window_id_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(return_type == AIVM_VAL_NODE) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(51U, ui_window_id_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate("sys.ui_getWindowSize", ui_window_id_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(return_type == AIVM_VAL_NODE) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(58U, ui_window_id_arg, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }

    draw_text_args[0] = aivm_value_int(10);
    draw_text_args[1] = aivm_value_int(20);
    draw_text_args[2] = aivm_value_int(0);
    draw_text_args[3] = aivm_value_string("hello");
    draw_text_args[4] = aivm_value_string("#000");
    draw_text_args[5] = aivm_value_int(14);

    if (expect(aivm_syscall_contract_validate("sys.ui_drawText", draw_text_args, 6U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }

    if (expect(aivm_syscall_contract_validate("sys.ui_drawText", draw_rect_args, 6U, &return_type) == AIVM_CONTRACT_ERR_ARG_TYPE) != 0) {
        return 1;
    }

    if (expect(aivm_syscall_contract_validate("sys.ui_drawText", draw_rect_args, 4U, &return_type) == AIVM_CONTRACT_ERR_ARG_COUNT) != 0) {
        return 1;
    }

    if (expect(aivm_syscall_contract_validate("sys.ui_drawText", NULL, 6U, &return_type) == AIVM_CONTRACT_ERR_ARG_TYPE) != 0) {
        return 1;
    }

    draw_text_args[3] = aivm_value_int(1);
    if (expect(aivm_syscall_contract_validate("sys.ui_drawText", draw_text_args, 6U, &return_type) == AIVM_CONTRACT_ERR_ARG_TYPE) != 0) {
        return 1;
    }
    draw_text_args[3] = aivm_value_string("hello");

    if (expect(aivm_syscall_contract_validate("sys.unknown", draw_text_args, 6U, &return_type) == AIVM_CONTRACT_ERR_UNKNOWN_TARGET) != 0) {
        return 1;
    }

    draw_line_args[0] = aivm_value_int(0);
    draw_line_args[1] = aivm_value_int(0);
    draw_line_args[2] = aivm_value_int(20);
    draw_line_args[3] = aivm_value_int(20);
    draw_line_args[4] = aivm_value_int(2);
    draw_line_args[5] = aivm_value_string("#f00");
    draw_line_args[6] = aivm_value_int(255);
    if (expect(aivm_syscall_contract_validate("sys.ui_drawLine", draw_line_args, 7U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(54U, draw_line_args, 7U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }

    utf8_count_args[0] = aivm_value_string("abc");
    if (expect(aivm_syscall_contract_validate("sys.str_utf8ByteCount", utf8_count_args, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(return_type == AIVM_VAL_INT) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(26U, utf8_count_args, 1U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }

    str_args[0] = aivm_value_string("hello");
    str_args[1] = aivm_value_int(1);
    str_args[2] = aivm_value_int(3);
    if (expect(aivm_syscall_contract_validate("sys.str_substring", str_args, 3U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(return_type == AIVM_VAL_STRING) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(59U, str_args, 3U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(60U, str_args, 3U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    str_args[1] = aivm_value_string("bad");
    if (expect(aivm_syscall_contract_validate("sys.str_remove", str_args, 3U, &return_type) == AIVM_CONTRACT_ERR_ARG_TYPE) != 0) {
        return 1;
    }

    if (expect(aivm_syscall_contract_validate_id(49U, draw_text_args, 3U, &return_type) == AIVM_CONTRACT_ERR_ARG_COUNT) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(49U, draw_text_args, 6U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    contract = aivm_syscall_contract_find_by_id(58U);
    if (expect(contract != NULL) != 0) {
        return 1;
    }
    if (expect(aivm_value_equals(aivm_value_string(contract->target), aivm_value_string("sys.ui_getWindowSize")) == 1) != 0) {
        return 1;
    }
    if (expect(contract->arg_count == 1U) != 0) {
        return 1;
    }
    if (expect(contract->return_type == AIVM_VAL_NODE) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(9999U, draw_text_args, 3U, &return_type) == AIVM_CONTRACT_ERR_UNKNOWN_ID) != 0) {
        return 1;
    }
    if (expect(aivm_value_equals(
            aivm_value_string(aivm_contract_status_message(AIVM_CONTRACT_ERR_UNKNOWN_ID)),
            aivm_value_string("Syscall contract ID was not found.")) == 1) != 0) {
        return 1;
    }

    return 0;
}
