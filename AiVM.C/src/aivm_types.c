#include "aivm_types.h"

AivmValue aivm_value_void(void)
{
    AivmValue value;
    value.type = AIVM_VAL_VOID;
    value.int_value = 0;
    return value;
}

AivmValue aivm_value_int(int64_t input)
{
    AivmValue value;
    value.type = AIVM_VAL_INT;
    value.int_value = input;
    return value;
}

AivmValue aivm_value_bool(int input)
{
    AivmValue value;
    value.type = AIVM_VAL_BOOL;
    value.bool_value = (input != 0) ? 1 : 0;
    return value;
}

AivmValue aivm_value_string(const char* input)
{
    AivmValue value;
    value.type = AIVM_VAL_STRING;
    value.string_value = input;
    return value;
}

int aivm_value_equals(AivmValue left, AivmValue right)
{
    if (left.type != right.type) {
        return 0;
    }

    switch (left.type) {
        case AIVM_VAL_VOID:
            return 1;

        case AIVM_VAL_INT:
            return left.int_value == right.int_value ? 1 : 0;

        case AIVM_VAL_BOOL:
            return left.bool_value == right.bool_value ? 1 : 0;

        case AIVM_VAL_STRING:
            return left.string_value == right.string_value ? 1 : 0;

        default:
            return 0;
    }
}
