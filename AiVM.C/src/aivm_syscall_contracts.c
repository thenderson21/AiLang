#include "aivm_syscall_contracts.h"

#include <string.h>

static const AivmSyscallContract Contracts[] = {
    { 6U, "sys.console_write", 1U, { AIVM_VAL_STRING, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 7U, "sys.console_writeLine", 1U, { AIVM_VAL_STRING, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 8U, "sys.console_readLine", 0U, { AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_STRING },
    { 9U, "sys.console_readAllStdin", 0U, { AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_STRING },
    { 10U, "sys.console_writeErrLine", 1U, { AIVM_VAL_STRING, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 16U, "sys.stdout_writeLine", 1U, { AIVM_VAL_STRING, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 11U, "sys.process_cwd", 0U, { AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_STRING },
    { 12U, "sys.process_envGet", 1U, { AIVM_VAL_STRING, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_STRING },
    { 18U, "sys.process_argv", 0U, { AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_NODE },
    { 28U, "sys.platform", 0U, { AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_STRING },
    { 29U, "sys.arch", 0U, { AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_STRING },
    { 30U, "sys.os_version", 0U, { AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_STRING },
    { 31U, "sys.runtime", 0U, { AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_STRING },
    { 46U, "sys.ui_createWindow", 3U, { AIVM_VAL_STRING, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_INT },
    { 47U, "sys.ui_beginFrame", 1U, { AIVM_VAL_INT, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 48U, "sys.ui_drawRect", 6U, { AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_STRING, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 49U, "sys.ui_drawText", 6U, { AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_STRING, AIVM_VAL_STRING, AIVM_VAL_INT, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 50U, "sys.ui_endFrame", 1U, { AIVM_VAL_INT, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 51U, "sys.ui_pollEvent", 1U, { AIVM_VAL_INT, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_NODE },
    { 52U, "sys.ui_present", 1U, { AIVM_VAL_INT, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 53U, "sys.ui_closeWindow", 1U, { AIVM_VAL_INT, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 54U, "sys.ui_drawLine", 7U, { AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_STRING, AIVM_VAL_INT, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 55U, "sys.ui_drawEllipse", 6U, { AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_STRING, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 56U, "sys.ui_drawPath", 4U, { AIVM_VAL_INT, AIVM_VAL_STRING, AIVM_VAL_STRING, AIVM_VAL_INT, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 57U, "sys.ui_drawImage", 6U, { AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_STRING, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 58U, "sys.ui_getWindowSize", 1U, { AIVM_VAL_INT, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_NODE },
    { 72U, "sys.ui_waitFrame", 1U, { AIVM_VAL_INT, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_VOID },
    { 26U, "sys.str_utf8ByteCount", 1U, { AIVM_VAL_STRING, AIVM_VAL_VOID, AIVM_VAL_VOID, AIVM_VAL_VOID }, AIVM_VAL_INT },
    { 59U, "sys.str_substring", 3U, { AIVM_VAL_STRING, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_VOID }, AIVM_VAL_STRING },
    { 60U, "sys.str_remove", 3U, { AIVM_VAL_STRING, AIVM_VAL_INT, AIVM_VAL_INT, AIVM_VAL_VOID }, AIVM_VAL_STRING }
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
