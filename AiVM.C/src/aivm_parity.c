#include "aivm_parity.h"

#include <string.h>

size_t aivm_parity_normalize_text(const char* input, char* output, size_t output_capacity)
{
    size_t in_index;
    size_t out_index;

    if (input == NULL || output == NULL || output_capacity == 0U) {
        return 0U;
    }

    in_index = 0U;
    out_index = 0U;

    while (input[in_index] != '\0') {
        if (input[in_index] == '\r' && input[in_index + 1U] == '\n') {
            if (out_index + 1U >= output_capacity) {
                break;
            }
            output[out_index] = '\n';
            out_index += 1U;
            in_index += 2U;
            continue;
        }

        if (out_index + 1U >= output_capacity) {
            break;
        }
        output[out_index] = input[in_index];
        out_index += 1U;
        in_index += 1U;
    }

    while (out_index > 0U && output[out_index - 1U] == '\n') {
        out_index -= 1U;
    }

    output[out_index] = '\0';
    return out_index;
}

int aivm_parity_equal_normalized(const char* left, const char* right)
{
    char left_buffer[4096];
    char right_buffer[4096];

    if (left == NULL || right == NULL) {
        return 0;
    }

    (void)aivm_parity_normalize_text(left, left_buffer, sizeof(left_buffer));
    (void)aivm_parity_normalize_text(right, right_buffer, sizeof(right_buffer));

    return strcmp(left_buffer, right_buffer) == 0 ? 1 : 0;
}
