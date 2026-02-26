#include "aivm_program.h"

static uint32_t read_u32_le(const uint8_t* bytes, size_t offset)
{
    return (uint32_t)bytes[offset] |
           ((uint32_t)bytes[offset + 1U] << 8U) |
           ((uint32_t)bytes[offset + 2U] << 16U) |
           ((uint32_t)bytes[offset + 3U] << 24U);
}

void aivm_program_clear(AivmProgram* program)
{
    if (program == NULL) {
        return;
    }

    program->instructions = NULL;
    program->instruction_count = 0U;
    program->format_version = 0U;
    program->format_flags = 0U;
    program->section_count = 0U;
}

void aivm_program_init(AivmProgram* program, const AivmInstruction* instructions, size_t instruction_count)
{
    if (program == NULL) {
        return;
    }

    program->instructions = instructions;
    program->instruction_count = instruction_count;
    program->format_version = 0U;
    program->format_flags = 0U;
    program->section_count = 0U;
}

AivmProgramLoadResult aivm_program_load_aibc1(const uint8_t* bytes, size_t byte_count, AivmProgram* out_program)
{
    AivmProgramLoadResult result;

    if (out_program != NULL) {
        aivm_program_clear(out_program);
    }

    if (bytes == NULL || out_program == NULL) {
        result.status = AIVM_PROGRAM_ERR_NULL;
        result.error_offset = 0U;
        return result;
    }

    /*
     * Deterministic header parse for AiBC1 prefix.
     * Full instruction decode remains deferred to later phases.
     */
    if (byte_count < 16U) {
        result.status = AIVM_PROGRAM_ERR_TRUNCATED;
        result.error_offset = byte_count;
        return result;
    }

    if (bytes[0] != (uint8_t)'A' ||
        bytes[1] != (uint8_t)'I' ||
        bytes[2] != (uint8_t)'B' ||
        bytes[3] != (uint8_t)'C') {
        result.status = AIVM_PROGRAM_ERR_BAD_MAGIC;
        result.error_offset = 0U;
        return result;
    }

    out_program->format_version = read_u32_le(bytes, 4U);
    out_program->format_flags = read_u32_le(bytes, 8U);
    out_program->section_count = read_u32_le(bytes, 12U);

    if (out_program->format_version != 1U) {
        result.status = AIVM_PROGRAM_ERR_UNSUPPORTED;
        result.error_offset = 4U;
        return result;
    }

    {
        size_t cursor = 16U;
        uint32_t section_index;
        for (section_index = 0U; section_index < out_program->section_count; section_index += 1U) {
            uint32_t section_size;
            if (cursor + 8U > byte_count) {
                result.status = AIVM_PROGRAM_ERR_TRUNCATED;
                result.error_offset = cursor;
                return result;
            }

            /* Section type field reserved for later decode phases. */
            (void)read_u32_le(bytes, cursor);
            section_size = read_u32_le(bytes, cursor + 4U);
            cursor += 8U;

            if (cursor + (size_t)section_size > byte_count) {
                result.status = AIVM_PROGRAM_ERR_SECTION_OOB;
                result.error_offset = cursor;
                return result;
            }

            cursor += (size_t)section_size;
        }
    }

    result.status = AIVM_PROGRAM_ERR_UNSUPPORTED;
    result.error_offset = 16U;
    return result;
}
