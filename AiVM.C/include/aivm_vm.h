#ifndef AIVM_VM_H
#define AIVM_VM_H

#include <stddef.h>

#include "aivm_program.h"

typedef struct {
    const AivmProgram* program;
    size_t instruction_pointer;
} AivmVm;

void aivm_init(AivmVm* vm, const AivmProgram* program);
void aivm_step(AivmVm* vm);
void aivm_run(AivmVm* vm);

#endif
