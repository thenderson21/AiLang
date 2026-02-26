#ifndef AIVM_TYPES_H
#define AIVM_TYPES_H

#include <stdint.h>

typedef enum {
    AIVM_VAL_VOID = 0,
    AIVM_VAL_INT = 1,
    AIVM_VAL_BOOL = 2,
    AIVM_VAL_STRING = 3
} AivmValueType;

typedef struct {
    AivmValueType type;
    union {
        int64_t int_value;
        int bool_value;
        const char* string_value;
    };
} AivmValue;

#endif
