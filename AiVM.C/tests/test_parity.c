#include <string.h>

#include "aivm_parity.h"

static int expect(int condition)
{
    return condition ? 0 : 1;
}

int main(void)
{
    char out[64];
    size_t length;

    length = aivm_parity_normalize_text("line1\r\nline2\r\n", out, sizeof(out));
    if (expect(length == 11U) != 0) {
        return 1;
    }
    if (expect(strcmp(out, "line1\nline2") == 0) != 0) {
        return 1;
    }

    if (expect(aivm_parity_equal_normalized("a\r\nb\n", "a\nb") == 1) != 0) {
        return 1;
    }

    if (expect(aivm_parity_equal_normalized("a\nb", "a\nb\n") == 1) != 0) {
        return 1;
    }

    if (expect(aivm_parity_equal_normalized("left", "right") == 0) != 0) {
        return 1;
    }

    return 0;
}
