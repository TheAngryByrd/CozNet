cmake_minimum_required(VERSION 3.20)
project(CozNetProfiler LANGUAGES C CXX)
if(NOT CMAKE_SYSTEM_NAME STREQUAL "Linux" OR NOT CMAKE_SYSTEM_PROCESSOR STREQUAL "x86_64")
    message(FATAL_ERROR "This native build requires Linux x64.")
endif()
if(NOT DEFINED RUNTIME_SOURCE OR NOT DEFINED GENERATED_INCLUDE)
    message(FATAL_ERROR "Set RUNTIME_SOURCE and GENERATED_INCLUDE.")
endif()
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CLR_SRC_NATIVE_DIR "${RUNTIME_SOURCE}/src/native")
add_compile_definitions(SOS_INCLUDE HOST_UNIX HOST_AMD64 HOST_64BIT TARGET_UNIX TARGET_AMD64 TARGET_64BIT)
include_directories(
    "${RUNTIME_SOURCE}/src/coreclr/pal/prebuilt/inc"
    "${RUNTIME_SOURCE}/src/coreclr/pal/inc/rt"
    "${RUNTIME_SOURCE}/src/coreclr/pal/inc"
    "${RUNTIME_SOURCE}/src/coreclr/inc"
    "${RUNTIME_SOURCE}/src/native"
    "${RUNTIME_SOURCE}/src/tests/profiler/native/rejitprofiler"
    "${GENERATED_INCLUDE}")
add_subdirectory("${RUNTIME_SOURCE}/src/native/minipal" minipal)
add_library(CozNet.Profiler SHARED
    Profiler.cpp SnapshotBackend.Unsupported.cpp
    "${RUNTIME_SOURCE}/src/tests/profiler/native/guids.cpp"
    "${RUNTIME_SOURCE}/src/tests/profiler/native/rejitprofiler/ilrewriter.cpp"
    "${RUNTIME_SOURCE}/src/tests/profiler/native/rejitprofiler/sigparse.cpp")
target_link_libraries(CozNet.Profiler PRIVATE minipal pthread dl)
target_link_options(CozNet.Profiler PRIVATE -Wl,-z,defs)
