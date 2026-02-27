#include "aivm_syscall_contracts.h"

#include <string.h>

static const AivmSyscallContract Contracts[] = {
    { 1001U, "sys.ui_drawRect", 4U, { AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT }, AIVM_VAL_VOID },
    { 1002U, "sys.ui_drawText", 3U, { AIVM_VAL_STRING, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 1003U, "sys.ui_getWindowSize", 0U, { AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_STRING },
    { 1100U, "sys.str_utf8ByteCount", 1U, { AIVM_VAL_STRING, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_INT },
    { 1101U, "sys.str_substring", 3U, { AIVM_VAL_STRING, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_VOID }, AIVM_VAL_STRING },
    { 1102U, "sys.str_remove", 3U, { AIVM_VAL_STRING, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_VOID }, AIVM_VAL_STRING }
};

static AivmContractStatus validate_contract(
    const AivmSyscallContract* contract,
    const AivmValue* args,
    size_t arg_count,
    AivmValueType* out_return_type)
{
    size_t arg_index;

    if (out_return_type != NULL) {
        *out_return_type = AIVM_VAL_VOID;
    }

    if (contract == NULL) {
        return AIVM_CONTRACT_ERR_UNKNOWN_TARGET;
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

const AivmSyscallContract* aivm_syscall_contract_find_by_target(const char* target)
{
    size_t index;

    if (target == NULL) {
        return NULL;
    }

    for (index = 0U; index < (sizeof(Contracts) / sizeof(Contracts[0])); index += 1U) {
        if (strcmp(Contracts[index].target, target) == 0) {
            return &Contracts[index];
        }
    }

    return NULL;
}

const AivmSyscallContract* aivm_syscall_contract_find_by_id(uint32_t id)
{
    size_t index;
    for (index = 0U; index < (sizeof(Contracts) / sizeof(Contracts[0])); index += 1U) {
        if (Contracts[index].id == id) {
            return &Contracts[index];
        }
    }
    return NULL;
}

AivmContractStatus aivm_syscall_contract_validate(
    const char* target,
    const AivmValue* args,
    size_t arg_count,
    AivmValueType* out_return_type)
{
    const AivmSyscallContract* contract = aivm_syscall_contract_find_by_target(target);
    if (contract == NULL) {
        return AIVM_CONTRACT_ERR_UNKNOWN_TARGET;
    }
    return validate_contract(contract, args, arg_count, out_return_type);
}

AivmContractStatus aivm_syscall_contract_validate_id(
    uint32_t id,
    const AivmValue* args,
    size_t arg_count,
    AivmValueType* out_return_type)
{
    const AivmSyscallContract* contract = aivm_syscall_contract_find_by_id(id);
    if (contract == NULL) {
        if (out_return_type != NULL) {
            *out_return_type = AIVM_VAL_VOID;
        }
        return AIVM_CONTRACT_ERR_UNKNOWN_ID;
    }
    return validate_contract(contract, args, arg_count, out_return_type);
}

const char* aivm_contract_status_code(AivmContractStatus status)
{
    switch (status) {
        case AIVM_CONTRACT_OK:
            return "AIVMC000";
        case AIVM_CONTRACT_ERR_UNKNOWN_TARGET:
            return "AIVMC001";
        case AIVM_CONTRACT_ERR_ARG_COUNT:
            return "AIVMC002";
        case AIVM_CONTRACT_ERR_ARG_TYPE:
            return "AIVMC003";
        case AIVM_CONTRACT_ERR_UNKNOWN_ID:
            return "AIVMC004";
        default:
            return "AIVMC999";
    }
}

const char* aivm_contract_status_message(AivmContractStatus status)
{
    switch (status) {
        case AIVM_CONTRACT_OK:
            return "Syscall contract validation passed.";
        case AIVM_CONTRACT_ERR_UNKNOWN_TARGET:
            return "Syscall target was not found.";
        case AIVM_CONTRACT_ERR_ARG_COUNT:
            return "Syscall argument count was invalid.";
        case AIVM_CONTRACT_ERR_ARG_TYPE:
            return "Syscall argument type was invalid.";
        case AIVM_CONTRACT_ERR_UNKNOWN_ID:
            return "Syscall contract ID was not found.";
        default:
            return "Unknown syscall contract validation status.";
    }
}
