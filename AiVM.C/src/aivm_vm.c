#include "aivm_vm.h"

static int operand_to_index(AivmVm* vm, int64_t operand, size_t* out_index)
{
    if (vm == NULL || out_index == NULL) {
        return 0;
    }

    if (operand < 0) {
        vm->error = AIVM_VM_ERR_INVALID_PROGRAM;
        vm->status = AIVM_VM_STATUS_ERROR;
        return 0;
    }

    *out_index = (size_t)operand;
    return 1;
}

static char* arena_alloc(AivmVm* vm, size_t size)
{
    char* start;
    if (vm == NULL) {
        return NULL;
    }
    if (vm->string_arena_used + size > AIVM_VM_STRING_ARENA_CAPACITY) {
        vm->error = AIVM_VM_ERR_STRING_OVERFLOW;
        vm->status = AIVM_VM_STATUS_ERROR;
        return NULL;
    }

    start = &vm->string_arena[vm->string_arena_used];
    vm->string_arena_used += size;
    return start;
}

static int push_string_copy(AivmVm* vm, const char* input)
{
    size_t length = 0U;
    size_t i;
    char* output;
    if (vm == NULL || input == NULL) {
        return 0;
    }
    while (input[length] != '\0') {
        length += 1U;
    }
    output = arena_alloc(vm, length + 1U);
    if (output == NULL) {
        return 0;
    }
    for (i = 0U; i < length; i += 1U) {
        output[i] = input[i];
    }
    output[length] = '\0';
    return aivm_stack_push(vm, aivm_value_string(output));
}

static int push_escaped_string(AivmVm* vm, const char* input)
{
    size_t length = 0U;
    size_t escaped_length = 0U;
    size_t i;
    size_t out_index = 0U;
    char* output;

    if (vm == NULL || input == NULL) {
        return 0;
    }

    while (input[length] != '\0') {
        char ch = input[length];
        if (ch == '\\' || ch == '"' || ch == '\n' || ch == '\r' || ch == '\t') {
            escaped_length += 2U;
        } else {
            escaped_length += 1U;
        }
        length += 1U;
    }

    output = arena_alloc(vm, escaped_length + 1U);
    if (output == NULL) {
        return 0;
    }

    for (i = 0U; i < length; i += 1U) {
        char ch = input[i];
        if (ch == '\\') {
            output[out_index++] = '\\';
            output[out_index++] = '\\';
        } else if (ch == '"') {
            output[out_index++] = '\\';
            output[out_index++] = '"';
        } else if (ch == '\n') {
            output[out_index++] = '\\';
            output[out_index++] = 'n';
        } else if (ch == '\r') {
            output[out_index++] = '\\';
            output[out_index++] = 'r';
        } else if (ch == '\t') {
            output[out_index++] = '\\';
            output[out_index++] = 't';
        } else {
            output[out_index++] = ch;
        }
    }

    output[out_index] = '\0';
    return aivm_stack_push(vm, aivm_value_string(output));
}

static size_t utf8_next_index(const char* text, size_t index)
{
    unsigned char ch;
    if (text == NULL || text[index] == '\0') {
        return index;
    }

    ch = (unsigned char)text[index];
    if ((ch & 0x80U) == 0U) {
        return index + 1U;
    }
    if ((ch & 0xE0U) == 0xC0U &&
        (text[index + 1U] & 0xC0) == 0x80) {
        return index + 2U;
    }
    if ((ch & 0xF0U) == 0xE0U &&
        (text[index + 1U] & 0xC0) == 0x80 &&
        (text[index + 2U] & 0xC0) == 0x80) {
        return index + 3U;
    }
    if ((ch & 0xF8U) == 0xF0U &&
        (text[index + 1U] & 0xC0) == 0x80 &&
        (text[index + 2U] & 0xC0) == 0x80 &&
        (text[index + 3U] & 0xC0) == 0x80) {
        return index + 4U;
    }
    return index + 1U;
}

static size_t utf8_rune_count(const char* text)
{
    size_t byte_index = 0U;
    size_t count = 0U;
    if (text == NULL) {
        return 0U;
    }
    while (text[byte_index] != '\0') {
        byte_index = utf8_next_index(text, byte_index);
        count += 1U;
    }
    return count;
}

static size_t utf8_byte_offset_for_rune(const char* text, size_t rune_index)
{
    size_t byte_index = 0U;
    size_t current_rune = 0U;
    if (text == NULL) {
        return 0U;
    }
    while (text[byte_index] != '\0' && current_rune < rune_index) {
        byte_index = utf8_next_index(text, byte_index);
        current_rune += 1U;
    }
    return byte_index;
}

static size_t clamp_rune_index(int64_t value, size_t max_value)
{
    if (value <= 0) {
        return 0U;
    }
    if ((uint64_t)value >= (uint64_t)max_value) {
        return max_value;
    }
    return (size_t)value;
}

static int push_substring_by_runes(AivmVm* vm, const char* text, int64_t start, int64_t length)
{
    size_t rune_count;
    size_t start_rune;
    size_t end_rune;
    size_t start_byte;
    size_t end_byte;
    size_t copy_length;
    size_t i;
    char* output;

    if (vm == NULL) {
        return 0;
    }
    if (text == NULL || length <= 0) {
        return push_string_copy(vm, "");
    }

    rune_count = utf8_rune_count(text);
    start_rune = clamp_rune_index(start, rune_count);
    end_rune = clamp_rune_index(start + length, rune_count);
    if (end_rune < start_rune) {
        end_rune = start_rune;
    }

    start_byte = utf8_byte_offset_for_rune(text, start_rune);
    end_byte = utf8_byte_offset_for_rune(text, end_rune);
    copy_length = end_byte - start_byte;
    output = arena_alloc(vm, copy_length + 1U);
    if (output == NULL) {
        return 0;
    }
    for (i = 0U; i < copy_length; i += 1U) {
        output[i] = text[start_byte + i];
    }
    output[copy_length] = '\0';
    return aivm_stack_push(vm, aivm_value_string(output));
}

static int push_remove_by_runes(AivmVm* vm, const char* text, int64_t start, int64_t length)
{
    size_t rune_count;
    size_t start_rune;
    size_t end_rune;
    size_t start_byte;
    size_t end_byte;
    size_t input_length = 0U;
    size_t output_length;
    size_t i;
    char* output;

    if (vm == NULL) {
        return 0;
    }
    if (text == NULL) {
        return push_string_copy(vm, "");
    }
    if (length <= 0) {
        return push_string_copy(vm, text);
    }

    while (text[input_length] != '\0') {
        input_length += 1U;
    }

    rune_count = utf8_rune_count(text);
    start_rune = clamp_rune_index(start, rune_count);
    end_rune = clamp_rune_index(start + length, rune_count);
    if (end_rune < start_rune) {
        end_rune = start_rune;
    }

    start_byte = utf8_byte_offset_for_rune(text, start_rune);
    end_byte = utf8_byte_offset_for_rune(text, end_rune);
    output_length = input_length - (end_byte - start_byte);
    output = arena_alloc(vm, output_length + 1U);
    if (output == NULL) {
        return 0;
    }

    for (i = 0U; i < start_byte; i += 1U) {
        output[i] = text[i];
    }
    for (; i < output_length; i += 1U) {
        output[i] = text[end_byte + (i - start_byte)];
    }
    output[output_length] = '\0';
    return aivm_stack_push(vm, aivm_value_string(output));
}

void aivm_reset_state(AivmVm* vm)
{
    if (vm == NULL) {
        return;
    }

    vm->instruction_pointer = 0U;
    vm->status = AIVM_VM_STATUS_READY;
    vm->error = AIVM_VM_ERR_NONE;
    vm->stack_count = 0U;
    vm->call_frame_count = 0U;
    vm->locals_count = 0U;
    vm->string_arena_used = 0U;
    vm->string_arena[0] = '\0';
}

void aivm_init(AivmVm* vm, const AivmProgram* program)
{
    if (vm == NULL) {
        return;
    }

    vm->program = program;
    vm->syscall_bindings = NULL;
    vm->syscall_binding_count = 0U;
    aivm_reset_state(vm);
}

void aivm_init_with_syscalls(
    AivmVm* vm,
    const AivmProgram* program,
    const AivmSyscallBinding* bindings,
    size_t binding_count)
{
    if (vm == NULL) {
        return;
    }

    vm->program = program;
    vm->syscall_bindings = bindings;
    vm->syscall_binding_count = binding_count;
    aivm_reset_state(vm);
}

void aivm_halt(AivmVm* vm)
{
    if (vm == NULL || vm->program == NULL) {
        return;
    }

    vm->instruction_pointer = vm->program->instruction_count;
    vm->status = AIVM_VM_STATUS_HALTED;
}

int aivm_stack_push(AivmVm* vm, AivmValue value)
{
    if (vm == NULL) {
        return 0;
    }

    if (vm->stack_count >= AIVM_VM_STACK_CAPACITY) {
        vm->error = AIVM_VM_ERR_STACK_OVERFLOW;
        vm->status = AIVM_VM_STATUS_ERROR;
        return 0;
    }

    vm->stack[vm->stack_count] = value;
    vm->stack_count += 1U;
    return 1;
}

int aivm_stack_pop(AivmVm* vm, AivmValue* out_value)
{
    if (vm == NULL || out_value == NULL) {
        return 0;
    }

    if (vm->stack_count == 0U) {
        vm->error = AIVM_VM_ERR_STACK_UNDERFLOW;
        vm->status = AIVM_VM_STATUS_ERROR;
        return 0;
    }

    vm->stack_count -= 1U;
    *out_value = vm->stack[vm->stack_count];
    return 1;
}

int aivm_frame_push(AivmVm* vm, size_t return_instruction_pointer, size_t frame_base)
{
    if (vm == NULL) {
        return 0;
    }

    if (vm->call_frame_count >= AIVM_VM_CALLFRAME_CAPACITY) {
        vm->error = AIVM_VM_ERR_FRAME_OVERFLOW;
        vm->status = AIVM_VM_STATUS_ERROR;
        return 0;
    }

    vm->call_frames[vm->call_frame_count].return_instruction_pointer = return_instruction_pointer;
    vm->call_frames[vm->call_frame_count].frame_base = frame_base;
    vm->call_frame_count += 1U;
    return 1;
}

int aivm_frame_pop(AivmVm* vm, AivmCallFrame* out_frame)
{
    if (vm == NULL || out_frame == NULL) {
        return 0;
    }

    if (vm->call_frame_count == 0U) {
        vm->error = AIVM_VM_ERR_FRAME_UNDERFLOW;
        vm->status = AIVM_VM_STATUS_ERROR;
        return 0;
    }

    vm->call_frame_count -= 1U;
    *out_frame = vm->call_frames[vm->call_frame_count];
    return 1;
}

int aivm_local_set(AivmVm* vm, size_t index, AivmValue value)
{
    if (vm == NULL) {
        return 0;
    }

    if (index >= AIVM_VM_LOCALS_CAPACITY) {
        vm->error = AIVM_VM_ERR_LOCAL_OUT_OF_RANGE;
        vm->status = AIVM_VM_STATUS_ERROR;
        return 0;
    }

    vm->locals[index] = value;
    if (index >= vm->locals_count) {
        vm->locals_count = index + 1U;
    }
    return 1;
}

int aivm_local_get(const AivmVm* vm, size_t index, AivmValue* out_value)
{
    if (vm == NULL || out_value == NULL) {
        return 0;
    }

    if (index >= vm->locals_count) {
        return 0;
    }

    *out_value = vm->locals[index];
    return 1;
}

void aivm_step(AivmVm* vm)
{
    const AivmInstruction* instruction;

    if (vm == NULL || vm->program == NULL) {
        return;
    }

    if (vm->program->instructions == NULL) {
        if (vm->program->instruction_count == 0U) {
            vm->status = AIVM_VM_STATUS_HALTED;
            return;
        }
        vm->error = AIVM_VM_ERR_INVALID_PROGRAM;
        vm->status = AIVM_VM_STATUS_ERROR;
        vm->instruction_pointer = vm->program->instruction_count;
        return;
    }

    if (vm->status == AIVM_VM_STATUS_HALTED || vm->status == AIVM_VM_STATUS_ERROR) {
        return;
    }

    if (vm->instruction_pointer >= vm->program->instruction_count) {
        vm->status = AIVM_VM_STATUS_HALTED;
        return;
    }

    vm->status = AIVM_VM_STATUS_RUNNING;
    instruction = &vm->program->instructions[vm->instruction_pointer];

    switch (instruction->opcode) {
        case AIVM_OP_NOP:
            vm->instruction_pointer += 1U;
            break;

        case AIVM_OP_HALT:
            aivm_halt(vm);
            break;

        case AIVM_OP_STUB:
            vm->instruction_pointer += 1U;
            break;

        case AIVM_OP_PUSH_INT:
            if (!aivm_stack_push(vm, aivm_value_int(instruction->operand_int))) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer += 1U;
            break;

        case AIVM_OP_POP: {
            AivmValue popped;
            if (!aivm_stack_pop(vm, &popped)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer += 1U;
            break;
        }

        case AIVM_OP_STORE_LOCAL: {
            AivmValue popped;
            size_t local_index;
            if (!operand_to_index(vm, instruction->operand_int, &local_index)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (!aivm_stack_pop(vm, &popped)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (!aivm_local_set(vm, local_index, popped)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer += 1U;
            break;
        }

        case AIVM_OP_LOAD_LOCAL: {
            AivmValue local_value;
            size_t local_index;
            if (!operand_to_index(vm, instruction->operand_int, &local_index)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (!aivm_local_get(vm, local_index, &local_value)) {
                vm->error = AIVM_VM_ERR_LOCAL_OUT_OF_RANGE;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (!aivm_stack_push(vm, local_value)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer += 1U;
            break;
        }

        case AIVM_OP_ADD_INT: {
            AivmValue right;
            AivmValue left;
            if (!aivm_stack_pop(vm, &right) || !aivm_stack_pop(vm, &left)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (left.type != AIVM_VAL_INT || right.type != AIVM_VAL_INT) {
                vm->error = AIVM_VM_ERR_TYPE_MISMATCH;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (!aivm_stack_push(vm, aivm_value_int(left.int_value + right.int_value))) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer += 1U;
            break;
        }

        case AIVM_OP_JUMP: {
            size_t target;
            if (!operand_to_index(vm, instruction->operand_int, &target)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (target > vm->program->instruction_count) {
                vm->error = AIVM_VM_ERR_INVALID_PROGRAM;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer = target;
            break;
        }

        case AIVM_OP_JUMP_IF_FALSE: {
            AivmValue condition;
            size_t target;
            if (!operand_to_index(vm, instruction->operand_int, &target)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (!aivm_stack_pop(vm, &condition)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (condition.type != AIVM_VAL_BOOL) {
                vm->error = AIVM_VM_ERR_TYPE_MISMATCH;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (condition.bool_value == 0) {
                if (target > vm->program->instruction_count) {
                    vm->error = AIVM_VM_ERR_INVALID_PROGRAM;
                    vm->status = AIVM_VM_STATUS_ERROR;
                    vm->instruction_pointer = vm->program->instruction_count;
                    break;
                }
                vm->instruction_pointer = target;
            } else {
                vm->instruction_pointer += 1U;
            }
            break;
        }

        case AIVM_OP_PUSH_BOOL:
            if (!aivm_stack_push(vm, aivm_value_bool((instruction->operand_int != 0) ? 1 : 0))) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer += 1U;
            break;

        case AIVM_OP_CALL: {
            size_t target;
            if (!operand_to_index(vm, instruction->operand_int, &target)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (target > vm->program->instruction_count) {
                vm->error = AIVM_VM_ERR_INVALID_PROGRAM;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (!aivm_frame_push(vm, vm->instruction_pointer + 1U, vm->stack_count)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer = target;
            break;
        }

        case AIVM_OP_RET:
        case AIVM_OP_RETURN: {
            AivmCallFrame frame;
            AivmValue return_value;
            int has_return_value = 0;
            if (!aivm_frame_pop(vm, &frame)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (frame.return_instruction_pointer > vm->program->instruction_count) {
                vm->error = AIVM_VM_ERR_INVALID_PROGRAM;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (vm->stack_count < frame.frame_base) {
                vm->error = AIVM_VM_ERR_INVALID_PROGRAM;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (vm->stack_count > frame.frame_base) {
                return_value = vm->stack[vm->stack_count - 1U];
                has_return_value = 1;
            }
            vm->stack_count = frame.frame_base;
            if (has_return_value != 0) {
                if (!aivm_stack_push(vm, return_value)) {
                    vm->instruction_pointer = vm->program->instruction_count;
                    break;
                }
            }
            vm->instruction_pointer = frame.return_instruction_pointer;
            break;
        }

        case AIVM_OP_EQ_INT: {
            AivmValue right;
            AivmValue left;
            if (!aivm_stack_pop(vm, &right) || !aivm_stack_pop(vm, &left)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (left.type != AIVM_VAL_INT || right.type != AIVM_VAL_INT) {
                vm->error = AIVM_VM_ERR_TYPE_MISMATCH;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (!aivm_stack_push(vm, aivm_value_bool(left.int_value == right.int_value ? 1 : 0))) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer += 1U;
            break;
        }

        case AIVM_OP_EQ: {
            AivmValue right;
            AivmValue left;
            if (!aivm_stack_pop(vm, &right) || !aivm_stack_pop(vm, &left)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (!aivm_stack_push(vm, aivm_value_bool(aivm_value_equals(left, right)))) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer += 1U;
            break;
        }

        case AIVM_OP_CONST: {
            size_t constant_index;
            if (!operand_to_index(vm, instruction->operand_int, &constant_index)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (vm->program->constants == NULL || constant_index >= vm->program->constant_count) {
                vm->error = AIVM_VM_ERR_INVALID_PROGRAM;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (!aivm_stack_push(vm, vm->program->constants[constant_index])) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer += 1U;
            break;
        }

        case AIVM_OP_STR_CONCAT: {
            AivmValue right;
            AivmValue left;
            size_t left_length = 0U;
            size_t right_length = 0U;
            size_t i;
            char* output;

            if (!aivm_stack_pop(vm, &right) || !aivm_stack_pop(vm, &left)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (left.type != AIVM_VAL_STRING ||
                right.type != AIVM_VAL_STRING ||
                left.string_value == NULL ||
                right.string_value == NULL) {
                vm->error = AIVM_VM_ERR_TYPE_MISMATCH;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }

            while (left.string_value[left_length] != '\0') {
                left_length += 1U;
            }
            while (right.string_value[right_length] != '\0') {
                right_length += 1U;
            }

            output = arena_alloc(vm, left_length + right_length + 1U);
            if (output == NULL) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }

            for (i = 0U; i < left_length; i += 1U) {
                output[i] = left.string_value[i];
            }
            for (i = 0U; i < right_length; i += 1U) {
                output[left_length + i] = right.string_value[i];
            }
            output[left_length + right_length] = '\0';

            if (!aivm_stack_push(vm, aivm_value_string(output))) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer += 1U;
            break;
        }

        case AIVM_OP_TO_STRING: {
            AivmValue value;
            char bool_buffer[6];
            char int_buffer[32];
            size_t int_index;
            uint64_t magnitude;
            int negative = 0;

            if (!aivm_stack_pop(vm, &value)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }

            if (value.type == AIVM_VAL_STRING) {
                if (value.string_value == NULL || !push_string_copy(vm, value.string_value)) {
                    vm->error = AIVM_VM_ERR_TYPE_MISMATCH;
                    vm->status = AIVM_VM_STATUS_ERROR;
                    vm->instruction_pointer = vm->program->instruction_count;
                    break;
                }
                vm->instruction_pointer += 1U;
                break;
            }
            if (value.type == AIVM_VAL_BOOL) {
                if (value.bool_value != 0) {
                    bool_buffer[0] = 't';
                    bool_buffer[1] = 'r';
                    bool_buffer[2] = 'u';
                    bool_buffer[3] = 'e';
                    bool_buffer[4] = '\0';
                } else {
                    bool_buffer[0] = 'f';
                    bool_buffer[1] = 'a';
                    bool_buffer[2] = 'l';
                    bool_buffer[3] = 's';
                    bool_buffer[4] = 'e';
                    bool_buffer[5] = '\0';
                }
                if (!push_string_copy(vm, bool_buffer)) {
                    vm->instruction_pointer = vm->program->instruction_count;
                    break;
                }
                vm->instruction_pointer += 1U;
                break;
            }
            if (value.type == AIVM_VAL_VOID) {
                if (!push_string_copy(vm, "null")) {
                    vm->instruction_pointer = vm->program->instruction_count;
                    break;
                }
                vm->instruction_pointer += 1U;
                break;
            }
            if (value.type == AIVM_VAL_INT) {
                int_index = sizeof(int_buffer) - 1U;
                int_buffer[int_index] = '\0';
                if (value.int_value < 0) {
                    negative = 1;
                    magnitude = (uint64_t)(-(value.int_value + 1)) + 1U;
                } else {
                    magnitude = (uint64_t)value.int_value;
                }
                do {
                    uint64_t digit = magnitude % 10U;
                    magnitude /= 10U;
                    int_index -= 1U;
                    int_buffer[int_index] = (char)('0' + (char)digit);
                } while (magnitude != 0U);
                if (negative != 0) {
                    int_index -= 1U;
                    int_buffer[int_index] = '-';
                }
                if (!push_string_copy(vm, &int_buffer[int_index])) {
                    vm->instruction_pointer = vm->program->instruction_count;
                    break;
                }
                vm->instruction_pointer += 1U;
                break;
            }

            vm->error = AIVM_VM_ERR_TYPE_MISMATCH;
            vm->status = AIVM_VM_STATUS_ERROR;
            vm->instruction_pointer = vm->program->instruction_count;
            break;
        }

        case AIVM_OP_STR_ESCAPE: {
            AivmValue value;
            if (!aivm_stack_pop(vm, &value)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (value.type != AIVM_VAL_STRING || value.string_value == NULL) {
                vm->error = AIVM_VM_ERR_TYPE_MISMATCH;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (!push_escaped_string(vm, value.string_value)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer += 1U;
            break;
        }

        case AIVM_OP_STR_SUBSTRING: {
            AivmValue length_value;
            AivmValue start_value;
            AivmValue text_value;
            if (!aivm_stack_pop(vm, &length_value) ||
                !aivm_stack_pop(vm, &start_value) ||
                !aivm_stack_pop(vm, &text_value)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (text_value.type != AIVM_VAL_STRING ||
                start_value.type != AIVM_VAL_INT ||
                length_value.type != AIVM_VAL_INT) {
                vm->error = AIVM_VM_ERR_TYPE_MISMATCH;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (!push_substring_by_runes(vm, text_value.string_value, start_value.int_value, length_value.int_value)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer += 1U;
            break;
        }

        case AIVM_OP_STR_REMOVE: {
            AivmValue length_value;
            AivmValue start_value;
            AivmValue text_value;
            if (!aivm_stack_pop(vm, &length_value) ||
                !aivm_stack_pop(vm, &start_value) ||
                !aivm_stack_pop(vm, &text_value)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (text_value.type != AIVM_VAL_STRING ||
                start_value.type != AIVM_VAL_INT ||
                length_value.type != AIVM_VAL_INT) {
                vm->error = AIVM_VM_ERR_TYPE_MISMATCH;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (!push_remove_by_runes(vm, text_value.string_value, start_value.int_value, length_value.int_value)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer += 1U;
            break;
        }

        case AIVM_OP_CALL_SYS: {
            size_t arg_count;
            AivmValue args[AIVM_VM_MAX_SYSCALL_ARGS];
            AivmValue target_value;
            AivmValue result;
            AivmSyscallStatus syscall_status;
            size_t i;

            if (!operand_to_index(vm, instruction->operand_int, &arg_count)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (arg_count > AIVM_VM_MAX_SYSCALL_ARGS) {
                vm->error = AIVM_VM_ERR_INVALID_PROGRAM;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }

            for (i = 0U; i < arg_count; i += 1U) {
                if (!aivm_stack_pop(vm, &args[arg_count - i - 1U])) {
                    vm->instruction_pointer = vm->program->instruction_count;
                    break;
                }
            }
            if (vm->status == AIVM_VM_STATUS_ERROR) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (!aivm_stack_pop(vm, &target_value)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (target_value.type != AIVM_VAL_STRING || target_value.string_value == NULL) {
                vm->error = AIVM_VM_ERR_TYPE_MISMATCH;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }

            syscall_status = aivm_syscall_dispatch_checked(
                vm->syscall_bindings,
                vm->syscall_binding_count,
                target_value.string_value,
                args,
                arg_count,
                &result);
            if (syscall_status != AIVM_SYSCALL_OK) {
                vm->error = AIVM_VM_ERR_SYSCALL;
                vm->status = AIVM_VM_STATUS_ERROR;
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            if (!aivm_stack_push(vm, result)) {
                vm->instruction_pointer = vm->program->instruction_count;
                break;
            }
            vm->instruction_pointer += 1U;
            break;
        }

        case AIVM_OP_ASYNC_CALL:
        case AIVM_OP_ASYNC_CALL_SYS:
        case AIVM_OP_AWAIT:
        case AIVM_OP_PAR_BEGIN:
        case AIVM_OP_PAR_FORK:
        case AIVM_OP_PAR_JOIN:
        case AIVM_OP_PAR_CANCEL:
            vm->error = AIVM_VM_ERR_INVALID_PROGRAM;
            vm->status = AIVM_VM_STATUS_ERROR;
            vm->instruction_pointer = vm->program->instruction_count;
            break;

        default:
            vm->error = AIVM_VM_ERR_INVALID_OPCODE;
            vm->status = AIVM_VM_STATUS_ERROR;
            vm->instruction_pointer = vm->program->instruction_count;
            break;
    }

    if (vm->status == AIVM_VM_STATUS_RUNNING &&
        vm->instruction_pointer >= vm->program->instruction_count) {
        vm->status = AIVM_VM_STATUS_HALTED;
    }
}

void aivm_run(AivmVm* vm)
{
    if (vm == NULL || vm->program == NULL) {
        return;
    }

    if (vm->program->instruction_count == 0U) {
        vm->status = AIVM_VM_STATUS_HALTED;
        return;
    }

    while (vm->instruction_pointer < vm->program->instruction_count &&
           vm->status != AIVM_VM_STATUS_ERROR &&
           vm->status != AIVM_VM_STATUS_HALTED) {
        aivm_step(vm);
    }
}

const char* aivm_vm_error_code(AivmVmError error)
{
    switch (error) {
        case AIVM_VM_ERR_NONE:
            return "AIVM000";
        case AIVM_VM_ERR_INVALID_OPCODE:
            return "AIVM001";
        case AIVM_VM_ERR_STACK_OVERFLOW:
            return "AIVM002";
        case AIVM_VM_ERR_STACK_UNDERFLOW:
            return "AIVM003";
        case AIVM_VM_ERR_FRAME_OVERFLOW:
            return "AIVM004";
        case AIVM_VM_ERR_FRAME_UNDERFLOW:
            return "AIVM005";
        case AIVM_VM_ERR_LOCAL_OUT_OF_RANGE:
            return "AIVM006";
        case AIVM_VM_ERR_TYPE_MISMATCH:
            return "AIVM007";
        case AIVM_VM_ERR_INVALID_PROGRAM:
            return "AIVM008";
        case AIVM_VM_ERR_STRING_OVERFLOW:
            return "AIVM009";
        case AIVM_VM_ERR_SYSCALL:
            return "AIVM010";
        default:
            return "AIVM999";
    }
}

const char* aivm_vm_error_message(AivmVmError error)
{
    switch (error) {
        case AIVM_VM_ERR_NONE:
            return "No error.";
        case AIVM_VM_ERR_INVALID_OPCODE:
            return "Invalid opcode.";
        case AIVM_VM_ERR_STACK_OVERFLOW:
            return "Stack overflow.";
        case AIVM_VM_ERR_STACK_UNDERFLOW:
            return "Stack underflow.";
        case AIVM_VM_ERR_FRAME_OVERFLOW:
            return "Call frame overflow.";
        case AIVM_VM_ERR_FRAME_UNDERFLOW:
            return "Call frame underflow.";
        case AIVM_VM_ERR_LOCAL_OUT_OF_RANGE:
            return "Local index out of range.";
        case AIVM_VM_ERR_TYPE_MISMATCH:
            return "Type mismatch.";
        case AIVM_VM_ERR_INVALID_PROGRAM:
            return "Invalid program state.";
        case AIVM_VM_ERR_STRING_OVERFLOW:
            return "VM string arena overflow.";
        case AIVM_VM_ERR_SYSCALL:
            return "Syscall dispatch failed.";
        default:
            return "Unknown VM error.";
    }
}
