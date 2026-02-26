cmake_minimum_required(VERSION 3.16)
project(aivm_c_core C)

set(CMAKE_C_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)

add_library(aivm_core STATIC
    src/aivm_vm.c
    src/aivm_program.c
    src/aivm_syscall.c
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
