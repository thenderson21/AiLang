#ifndef AIVM_PARITY_H
#define AIVM_PARITY_H

#include <stddef.h>

size_t aivm_parity_normalize_text(const char* input, char* output, size_t output_capacity);
int aivm_parity_equal_normalized(const char* left, const char* right);

#endif
