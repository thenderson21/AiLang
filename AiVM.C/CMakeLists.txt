cmake_minimum_required(VERSION 3.16)
project(aivm_c_core C)

set(CMAKE_C_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)

add_library(aivm_core STATIC
    src/aivm_types.c
    src/aivm_vm.c
    src/aivm_program.c
    src/aivm_syscall.c
    src/aivm_parity.c
)

target_include_directories(aivm_core PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

if (MSVC)
    target_compile_options(aivm_core PRIVATE /W4)
else()
    target_compile_options(aivm_core PRIVATE -Wall -Wextra -Wpedantic)
endif()

add_executable(hello_vm
    examples/hello_vm.c
)

target_link_libraries(hello_vm PRIVATE aivm_core)

if (MSVC)
    target_compile_options(hello_vm PRIVATE /W4)
else()
    target_compile_options(hello_vm PRIVATE -Wall -Wextra -Wpedantic)
endif()

include(CTest)
if (BUILD_TESTING)
    add_executable(aivm_test_program_loader
        tests/test_program_loader.c
    )
    target_link_libraries(aivm_test_program_loader PRIVATE aivm_core)

    add_executable(aivm_test_vm_core
        tests/test_vm_core.c
    )
    target_link_libraries(aivm_test_vm_core PRIVATE aivm_core)

    add_executable(aivm_test_values
        tests/test_values.c
    )
    target_link_libraries(aivm_test_values PRIVATE aivm_core)

    add_executable(aivm_test_syscall
        tests/test_syscall.c
    )
    target_link_libraries(aivm_test_syscall PRIVATE aivm_core)

    add_executable(aivm_test_vm_diagnostics
        tests/test_vm_diagnostics.c
    )
    target_link_libraries(aivm_test_vm_diagnostics PRIVATE aivm_core)

    add_executable(aivm_test_vm_stack
        tests/test_vm_stack.c
    )
    target_link_libraries(aivm_test_vm_stack PRIVATE aivm_core)

    add_executable(aivm_test_vm_frames_locals
        tests/test_vm_frames_locals.c
    )
    target_link_libraries(aivm_test_vm_frames_locals PRIVATE aivm_core)

    add_executable(aivm_test_vm_ops
        tests/test_vm_ops.c
    )
    target_link_libraries(aivm_test_vm_ops PRIVATE aivm_core)

    add_executable(aivm_test_parity
        tests/test_parity.c
    )
    target_link_libraries(aivm_test_parity PRIVATE aivm_core)

    if (MSVC)
        target_compile_options(aivm_test_program_loader PRIVATE /W4)
        target_compile_options(aivm_test_vm_core PRIVATE /W4)
        target_compile_options(aivm_test_values PRIVATE /W4)
        target_compile_options(aivm_test_syscall PRIVATE /W4)
        target_compile_options(aivm_test_vm_diagnostics PRIVATE /W4)
        target_compile_options(aivm_test_vm_stack PRIVATE /W4)
        target_compile_options(aivm_test_vm_frames_locals PRIVATE /W4)
        target_compile_options(aivm_test_vm_ops PRIVATE /W4)
        target_compile_options(aivm_test_parity PRIVATE /W4)
    else()
        target_compile_options(aivm_test_program_loader PRIVATE -Wall -Wextra -Wpedantic)
        target_compile_options(aivm_test_vm_core PRIVATE -Wall -Wextra -Wpedantic)
        target_compile_options(aivm_test_values PRIVATE -Wall -Wextra -Wpedantic)
        target_compile_options(aivm_test_syscall PRIVATE -Wall -Wextra -Wpedantic)
        target_compile_options(aivm_test_vm_diagnostics PRIVATE -Wall -Wextra -Wpedantic)
        target_compile_options(aivm_test_vm_stack PRIVATE -Wall -Wextra -Wpedantic)
        target_compile_options(aivm_test_vm_frames_locals PRIVATE -Wall -Wextra -Wpedantic)
        target_compile_options(aivm_test_vm_ops PRIVATE -Wall -Wextra -Wpedantic)
        target_compile_options(aivm_test_parity PRIVATE -Wall -Wextra -Wpedantic)
    endif()

    add_test(NAME aivm_test_program_loader COMMAND aivm_test_program_loader)
    add_test(NAME aivm_test_vm_core COMMAND aivm_test_vm_core)
    add_test(NAME aivm_test_values COMMAND aivm_test_values)
    add_test(NAME aivm_test_syscall COMMAND aivm_test_syscall)
    add_test(NAME aivm_test_vm_diagnostics COMMAND aivm_test_vm_diagnostics)
    add_test(NAME aivm_test_vm_stack COMMAND aivm_test_vm_stack)
    add_test(NAME aivm_test_vm_frames_locals COMMAND aivm_test_vm_frames_locals)
    add_test(NAME aivm_test_vm_ops COMMAND aivm_test_vm_ops)
    add_test(NAME aivm_test_parity COMMAND aivm_test_parity)
endif()
