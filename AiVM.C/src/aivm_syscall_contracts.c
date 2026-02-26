#include "aivm_syscall_contracts.h"

#include <string.h>

static const AivmSyscallContract Contracts[] = {
    { "sys.ui_drawRect", 4U, { AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT }, AIVM_VAL_VOID },
    { "sys.ui_drawText", 3U, { AIVM_VAL_STRING, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { "sys.ui_getWindowSize", 0U, { AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_STRING }
};

AivmContractStatus aivm_syscall_contract_validate(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValueType* out_return_type)
{
    size_t index;

    if (out_return_type != NULL) {
        *out_return_type = AIVM_VAL_VOID;
    }

    if (target == NULL) {
        return AIVM_CONTRACT_ERR_UNKNOWN_TARGET;
    }

    for (index = 0U; index < (sizeof(Contracts) / sizeof(Contracts[0])); index += 1U) {
        size_t arg_index;
        const AivmSyscallContract* contract = &Contracts[index];

        if (strcmp(contract->target, target) != 0) {
            continue;
        }

        if (contract->arg_count != arg_count) {
            return AIVM_CONTRACT_ERR_ARG_COUNT;
        }

        for (arg_index = 0U; arg_index < arg_count; arg_index += 1U) {
            if (args == NULL || args[arg_index].type != contract->arg_types[arg_index]) {
                return AIVM_CONTRACT_ERR_ARG_TYPE;
            }
        }

        if (out_return_type != NULL) {
            *out_return_type = contract->return_type;
        }
        return AIVM_CONTRACT_OK;
    }

    return AIVM_CONTRACT_ERR_UNKNOWN_TARGET;
}
