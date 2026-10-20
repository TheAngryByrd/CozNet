cmake_minimum_required(VERSION 3.20)
project(CozNetMapProfiler LANGUAGES C CXX)
if(NOT CMAKE_SYSTEM_NAME STREQUAL "Linux" OR NOT CMAKE_SYSTEM_PROCESSOR STREQUAL "x86_64")
    message(FATAL_ERROR "This native build requires Linux x64.")
endif()
if(NOT DEFINED RUNTIME_SOURCE OR NOT DEFINED GENERATED_INCLUDE)
    message(FATAL_ERROR "Set RUNTIME_SOURCE and GENERATED_INCLUDE.")
endif()
option(MAP_SANITIZE_THREAD "Build the checks with ThreadSanitizer." OFF)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CLR_SRC_NATIVE_DIR "${RUNTIME_SOURCE}/src/native")
add_compile_definitions(HOST_UNIX HOST_AMD64 HOST_64BIT TARGET_UNIX TARGET_AMD64 TARGET_64BIT)
set(MAP_WARNINGS -Wall -Wextra -Werror -Wno-unused-parameter)
include_directories(SYSTEM
    "${RUNTIME_SOURCE}/src/coreclr/pal/prebuilt/inc"
    "${RUNTIME_SOURCE}/src/coreclr/pal/inc/rt"
    "${RUNTIME_SOURCE}/src/coreclr/pal/inc"
    "${RUNTIME_SOURCE}/src/coreclr/inc"
    "${RUNTIME_SOURCE}/src/native")
include_directories("${GENERATED_INCLUDE}")
add_subdirectory("${RUNTIME_SOURCE}/src/native/minipal" minipal)
set(GUIDS "${RUNTIME_SOURCE}/src/tests/profiler/native/guids.cpp")

if(NOT MAP_SANITIZE_THREAD)
    add_library(CozNet.MapProfiler SHARED MapProfiler.Linux.cpp "${GUIDS}")
    target_compile_options(CozNet.MapProfiler PRIVATE -fvisibility=hidden)
    target_compile_options(CozNet.MapProfiler PRIVATE ${MAP_WARNINGS})
    target_link_libraries(CozNet.MapProfiler PRIVATE minipal pthread)
    target_link_options(CozNet.MapProfiler PRIVATE -Wl,-z,defs -Wl,--exclude-libs,ALL)
endif()

add_executable(MapCaptureChecks MapCaptureChecks.Linux.cpp MapProfiler.Linux.cpp "${GUIDS}")
target_compile_options(MapCaptureChecks PRIVATE ${MAP_WARNINGS})
target_link_libraries(MapCaptureChecks PRIVATE minipal pthread)
if(MAP_SANITIZE_THREAD)
    target_compile_options(MapCaptureChecks PRIVATE -fsanitize=thread -g -O1)
    # The compiler image has no clang TSan runtime, only the GCC libtsan.
    target_link_libraries(MapCaptureChecks PRIVATE /usr/lib/gcc/x86_64-linux-gnu/13/libtsan.so)
endif()
