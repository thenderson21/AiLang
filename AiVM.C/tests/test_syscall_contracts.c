#include "aivm_syscall_contracts.h"

static int expect(int condition)
{
    return condition ? 0 : 1;
}

int main(void)
{
    AivmValueType return_type;
    const AivmSyscallContract* contract;
    AivmValue draw_rect_args[4];
    AivmValue draw_text_args[3];

    draw_rect_args[0] = aivm_value_int(0);
    draw_rect_args[1] = aivm_value_int(0);
    draw_rect_args[2] = aivm_value_int(100);
    draw_rect_args[3] = aivm_value_int(50);

    if (expect(aivm_syscall_contract_validate("sys.ui_drawRect", draw_rect_args, 4U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    if (expect(return_type == AIVM_VAL_VOID) != 0) {
        return 1;
    }
    contract = aivm_syscall_contract_find_by_target("sys.ui_drawRect");
    if (expect(contract != NULL) != 0) {
        return 1;
    }
    if (expect(contract->id == 1001U) != 0) {
        return 1;
    }

    draw_text_args[0] = aivm_value_string("hello");
    draw_text_args[1] = aivm_value_int(10);
    draw_text_args[2] = aivm_value_int(20);

    if (expect(aivm_syscall_contract_validate("sys.ui_drawText", draw_text_args, 3U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }

    if (expect(aivm_syscall_contract_validate("sys.ui_drawText", draw_rect_args, 4U, &return_type) == AIVM_CONTRACT_ERR_ARG_COUNT) != 0) {
        return 1;
    }

    if (expect(aivm_syscall_contract_validate("sys.ui_drawText", NULL, 3U, &return_type) == AIVM_CONTRACT_ERR_ARG_TYPE) != 0) {
        return 1;
    }

    draw_text_args[0] = aivm_value_int(1);
    if (expect(aivm_syscall_contract_validate("sys.ui_drawText", draw_text_args, 3U, &return_type) == AIVM_CONTRACT_ERR_ARG_TYPE) != 0) {
        return 1;
    }

    if (expect(aivm_syscall_contract_validate("sys.unknown", draw_text_args, 3U, &return_type) == AIVM_CONTRACT_ERR_UNKNOWN_TARGET) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(1002U, draw_text_args, 3U, &return_type) == AIVM_CONTRACT_ERR_ARG_TYPE) != 0) {
        return 1;
    }
    draw_text_args[0] = aivm_value_string("hello");
    if (expect(aivm_syscall_contract_validate_id(1002U, draw_text_args, 3U, &return_type) == AIVM_CONTRACT_OK) != 0) {
        return 1;
    }
    contract = aivm_syscall_contract_find_by_id(1003U);
    if (expect(contract != NULL) != 0) {
        return 1;
    }
    if (expect(aivm_value_equals(aivm_value_string(contract->target), aivm_value_string("sys.ui_getWindowSize")) == 1) != 0) {
        return 1;
    }
    if (expect(aivm_syscall_contract_validate_id(9999U, draw_text_args, 3U, &return_type) == AIVM_CONTRACT_ERR_UNKNOWN_ID) != 0) {
        return 1;
    }

    return 0;
}
