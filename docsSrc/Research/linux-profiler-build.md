# Linux x64 profiler build

Date: 2026-10-13.
Runtime revision: `60629d14374c56f1cb51819049ad1fa529307f8d`.
This report combines source review with an isolated Linux x64 build and activation experiment.
`NativeLinuxVerify` now reproduces the activation and intervention checks through repository build adapters.

## Recommended route

Adapt the runtime test profiler's Linux build pattern.
Use its PAL compatibility headers, GUID definitions, and `minipal` support.
Do not start by building or linking the full CoreCLR PAL runtime.
The upstream test target links `pthread`, `${CMAKE_DL_LIBS}`, and inherited `minipal`, not a full PAL library. [R1][R2]

Use a pinned source checkout for the first experiment.
The Windows script's small downloaded header set omits the Linux compatibility tree and `src/native/minipal` headers. [L1][R1][R2]
Reduce the dependency set only after the compiler and linker identify the complete closure.

## Verified build requirements

| Area | Evidence from the pinned runtime | CozNet implication |
|---|---|---|
| Build tools | The native test target requires CMake 3.20. Its Linux branch supports Clang and other C++ compilers. [R1] | Provide C/C++ compilers, Linux libc development files, a C++ standard library, CMake, and a build tool. A .NET SDK alone does not establish these prerequisites. |
| Header paths | The target includes `src/coreclr/pal/prebuilt/inc`, `src/coreclr/pal/inc/rt`, `src/coreclr/pal/inc`, and `src/coreclr/inc`. Its parent includes `src/native`. [R1][R2] | Preserve these directory trees and include paths. The PAL `ole2.h`, `rpc.h`, and related headers replace Windows SDK declarations. [R3][R4] |
| Generated callback base | CozNet generates `CallbackBase.h` from the pinned prebuilt `corprof.h`. [L1] | Retain the existing callback-count checks and generation. The generated base is not a Windows SDK dependency. |
| Defines | The test target sets `SOS_INCLUDE`. Shared compiler setup sets `HOST_UNIX`, `HOST_AMD64`, `HOST_64BIT`, `TARGET_UNIX`, `TARGET_AMD64`, and `TARGET_64BIT` for this target. [R1][R5] | Use the Linux x64 definitions. Do not retain `WIN32` or `WIN64` from the Windows command. |
| C++ mode | CozNet requests C++17 and uses exceptions. The runtime common configuration defaults to C++11. [L1][R5] | Preserve C++17 and exception support for CozNet. Do not copy all runtime flags without checking their purpose. |
| Shared library | Upstream uses `add_library(Profiler SHARED ...)`. Linux dependencies are `pthread`, `dl`, and `minipal`. [R1][R2] | Produce an ELF `.so`. Use position-independent objects, including any linked static support. Do not pass the Windows `.def` file or `ole32.lib`. |
| IL rewriter | The same upstream target compiles `ilrewriter.cpp` and `sigparse.cpp` on Linux. The rewriter includes `corhlpr.cpp` itself. [R1][R6] | Reuse these pinned sources. Do not separately compile `corhlpr.cpp` into another definition. Retain and verify CozNet's existing local-type patch. [L1] |

This table describes a source-supported starting configuration, not a proven minimal command line.
CMake's runtime parent also configures `minipal` and common platform variables. [R2]
The profiler subdirectory alone is therefore not a complete standalone build recipe.

## UTF-16 and `WCHAR`

PAL defines `WCHAR` as `char16_t` for C++.
Its `W(str)` macro uses a `u` literal. [R7][R8]
The runtime test helper explicitly distinguishes Linux's 32-bit `wchar_t` from runtime 16-bit strings.
It supplies `char16_t` comparison functions and a platform-specific literal macro. [R9]

CozNet currently passes `WCHAR*` values to `wcscmp` and compares them with `L"..."` literals.
It also passes `L"Probe"` to `FindMethod`. [L2]
These uses need a UTF-16 adapter before a normal Linux build can type-check.
Use code-unit comparisons for these fixed ASCII identifiers and a platform-specific UTF-16 literal macro.
Do not reinterpret `char16_t*` as `wchar_t*`.
Do not use `-fshort-wchar` as a substitute for matching libc's wide-string ABI.
The upstream helper demonstrates the required separation directly. [R9]

## GUIDs and calling conventions

PAL maps `__uuidof(Type)` to `IID_Type`. It does not require Clang's Microsoft UUID extension. [R4]
The runtime test profiler compiles `guids.cpp`, which defines the COM and profiler interface identifiers. [R10]
CozNet's unconditional `<initguid.h>` include relies on the Windows SDK. [L2]
Use a platform-specific GUID include strategy and one GUID definition translation unit.
Avoid duplicate definitions if retaining Windows `INITGUID` behavior.
Metadata identifiers declared through PAL's `EXTERN_GUID` become header `constexpr` values. [R4]

GUID comparisons also have a link dependency.
The Linux `operator==` calls `minipal_guid_equals`, which `guid.c` implements. [R11][R12]
Compiling the entire `guid.c` object also introduces its random-byte dependency through GUID creation. [R12]
Use the upstream `minipal` target for the first build instead of assuming headers alone are sufficient. [R2][R13]

For non-i386 platforms, PAL defines `__stdcall` and `__cdecl` as empty macros.
PAL defines `STDMETHODCALLTYPE` as `__cdecl`. [R7][R14]
Thus Linux x64 uses the platform ABI for these declarations.
CozNet already selects ELF default visibility through `Exports.h` and gives exported functions C linkage. [L3]
Verify exported symbol names with `nm -D` before runtime activation.

## Reuse boundaries

The runtime test support provides a useful Linux build reference and reusable MIT-licensed source.
It is source inside the runtime repository, not a demonstrated standalone SDK for this project. [R1][R2][R15]
Reuse its compatibility pattern and selected helpers.
Do not compile its whole profiler factory into CozNet: it creates the runtime's test profilers. [R16]

`SnapshotBackend.h` already defines CozNet's platform boundary. [L4]
The current Windows implementation must not enter the Linux source list.
The first Linux build can supply a backend that explicitly reports unsupported sampling.
That permits startup and IL-rewriting checks without claiming Linux stack-snapshot support.
This is a proposed capability boundary, not a completed implementation.

## Next build experiment

1. Use a disposable Linux x64 build environment with CMake 3.20 or later, Clang or GCC, and Linux development libraries.
2. Obtain the pinned runtime source and configure its `minipal` support with the required generated configuration headers. [R13]
3. Compile CozNet's profiler, generated callback base, patched IL rewriter, signature parser, GUID unit, and explicit unsupported-sampling backend.
4. Use the five include roots above, C++17, `SOS_INCLUDE`, Linux x64 defines, PIC, and thread support.
5. Link a shared library with `minipal`, `pthread`, and `dl`. Add `-Wl,-z,defs` to expose unresolved dependencies immediately.
6. Inspect `nm -D` and `ldd -r` results. Require `DllGetClassObject` and the expected `Coz*` exports.
7. Start the controlled workload with the ELF profiler path. Keep tiered compilation and ReadyToRun disabled for this experiment.
8. Require one instrumented method, valid IL maps, unchanged checksums, and exact probe counts for the existing three loop sizes.
9. Run the known-credit intervention check separately. Keep sampling unsupported until a Linux backend passes its own checks.

The launch adapter must replace the Windows DLL path and `PATH` manipulation in `Verify.ps1`. [L5]
Use an absolute `CORECLR_PROFILER_PATH`; provide the same library to the workload's native imports.
Record compile errors, missing symbols, activation status, and workload output separately.
Successful compilation alone does not establish profiler activation or correct IL rewriting.

## Executed Linux experiment

An isolated Clang 18.1.3 build linked the profiler with the pinned runtime headers, GUID definitions, and `minipal`.
The linker used `-Wl,-z,defs`. It reported no unresolved symbols.
The source copy uses UTF-16 comparisons and guards the Windows GUID-header dependency.
It includes standard C++ headers before PAL headers.
PAL's `__valid` annotation otherwise conflicts with the libstdc++ numeric parser.
The snapshot backend returns `E_NOTIMPL` for capture and CPU-clock requests.

The .NET 10.0.0 Linux x64 runtime loaded the resulting shared library.
The existing instrumentation workload passed baseline, profiled, and known-credit intervention modes.
Baseline work executed zero probes.
Profiled counts of zero, one, and one million executed two, three, and 1,000,002 probes.
The profiler returned all 20 expected original-to-rewritten IL map entries.
The zero-credit and 20-millisecond intervention cases preserved the checksum and reached probe 10,000.
The positive case reported 22.61 milliseconds inside the managed check and 21.19 milliseconds of global delay.
These checks establish activation and controlled intervention. They do not establish Linux stack sampling or sampled prediction accuracy.

Evidence: [Linux build experiment](../../reliability/features/runtime-probes/data/linux-build-experiment/).
The source archive contains the isolated CMake configuration, Dockerfile, adjusted profiler source, and unsupported snapshot adapter.
The shared-library SHA-256 is `95D3C619FE796F8E99CD64491B694F4CDE54BF02178373D238DADC087A27EDC2`.
The shared native source now contains the verified portability changes.
The integrated target also verifies that the Linux snapshot adapter returns `E_NOTIMPL`.
Joining Linux CPU samples to automatic probes remains the next integration step.

## Sources

[L1]: ../../native/Build.ps1
[L2]: ../../native/Profiler.cpp
[L3]: ../../native/Exports.h
[L4]: ../../native/SnapshotBackend.h
[L5]: ../../native/Verify.ps1
[R1]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/tests/profiler/native/CMakeLists.txt
[R2]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/tests/CMakeLists.txt#L1-L19
[R3]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/inc/cor.h#L13-L18
[R4]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/pal/inc/rt/rpc.h#L14-L28
[R5]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/eng/native/configurecompiler.cmake
[R6]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/tests/profiler/native/rejitprofiler/ilrewriter.cpp#L4-L12
[R7]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/pal/inc/pal_mstypes.h
[R8]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/pal/inc/pal.h#L72-L77
[R9]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/tests/profiler/native/profilerstring.h#L12-L61
[R10]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/tests/profiler/native/guids.cpp
[R11]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/native/minipal/guid.h#L65-L74
[R12]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/native/minipal/guid.c#L32-L72
[R13]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/native/minipal/CMakeLists.txt
[R14]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/pal/inc/rt/palrt.h#L123-L163
[R15]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/LICENSE.TXT
[R16]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/tests/profiler/native/classfactory.cpp
