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
    src/aivm_syscall_contracts.c
    src/aivm_parity.c
    src/aivm_runtime.c
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

add_executable(aivm_parity_cli
    examples/parity_cli.c
)

target_link_libraries(aivm_parity_cli PRIVATE aivm_core)

if (MSVC)
    target_compile_options(aivm_parity_cli PRIVATE /W4)
else()
    target_compile_options(aivm_parity_cli PRIVATE -Wall -Wextra -Wpedantic)
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

    add_executable(aivm_test_runtime
        tests/test_runtime.c
    )
    target_link_libraries(aivm_test_runtime PRIVATE aivm_core)

    add_executable(aivm_test_syscall_contracts
        tests/test_syscall_contracts.c
    )
    target_link_libraries(aivm_test_syscall_contracts PRIVATE aivm_core)

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
        target_compile_options(aivm_test_runtime PRIVATE /W4)
        target_compile_options(aivm_test_syscall_contracts PRIVATE /W4)
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
        target_compile_options(aivm_test_runtime PRIVATE -Wall -Wextra -Wpedantic)
        target_compile_options(aivm_test_syscall_contracts PRIVATE -Wall -Wextra -Wpedantic)
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
    add_test(NAME aivm_test_runtime COMMAND aivm_test_runtime)
    add_test(NAME aivm_test_syscall_contracts COMMAND aivm_test_syscall_contracts)
    add_test(
        NAME aivm_test_parity_cli
        COMMAND aivm_parity_cli
            ${CMAKE_CURRENT_SOURCE_DIR}/tests/parity_left.txt
            ${CMAKE_CURRENT_SOURCE_DIR}/tests/parity_right.txt
    )
endif()
