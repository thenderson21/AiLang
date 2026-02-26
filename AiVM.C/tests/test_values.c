#include "aivm_types.h"

static int expect(int condition)
{
    return condition ? 0 : 1;
}

int main(void)
{
    AivmValue void_value;
    AivmValue int_value;
    AivmValue true_value;
    AivmValue false_value;
    AivmValue string_a;
    AivmValue string_b;

    void_value = aivm_value_void();
    if (expect(void_value.type == AIVM_VAL_VOID) != 0) {
        return 1;
    }

    int_value = aivm_value_int(42);
    if (expect(int_value.type == AIVM_VAL_INT) != 0) {
        return 1;
    }
    if (expect(int_value.int_value == 42) != 0) {
        return 1;
    }

    true_value = aivm_value_bool(7);
    false_value = aivm_value_bool(0);
    if (expect(true_value.bool_value == 1) != 0) {
        return 1;
    }
    if (expect(false_value.bool_value == 0) != 0) {
        return 1;
    }

    string_a = aivm_value_string("hello");
    string_b = aivm_value_string("hello");

    if (expect(aivm_value_equals(void_value, aivm_value_void()) == 1) != 0) {
        return 1;
    }
    if (expect(aivm_value_equals(int_value, aivm_value_int(42)) == 1) != 0) {
        return 1;
    }
    if (expect(aivm_value_equals(true_value, false_value) == 0) != 0) {
        return 1;
    }
    if (expect(aivm_value_equals(string_a, string_b) == 1) != 0) {
        return 1;
    }
    if (expect(aivm_value_equals(string_a, aivm_value_string("world")) == 0) != 0) {
        return 1;
    }

    return 0;
}
