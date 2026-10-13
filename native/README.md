# Native profiler prototype

The build supports Windows x64 and Linux x64 startup instrumentation.
Stack snapshots remain Windows x64 only. macOS support remains incomplete.
The default target is the instrumentation workload. `COZNET_PROBE_TARGET=linux-perf` selects the Linux CPU workload instead.
`LinuxInjectedVerify` connects that workload's CPU samples to injected callbacks on two threads.
Dense probe cost is excessive. See [the measured comparison](../reliability/features/linux-perf/injected-probes.md).
`LinuxCountdownVerify` compares conditional probes at intervals 64, 256, and 1024.
See [countdown results](../reliability/features/linux-perf/countdown-probes.md) for cost and attribution limits.

`Sampler.h` uses standard C++ threads, mutexes, and a condition variable.
`SnapshotBackend.h` defines the platform boundary for stack capture.
The sampler owns the backend for the sampling session.
`Capture` must complete all frame callbacks before it returns.
It must resume any thread that it suspends, including on failure.
Callbacks must not allocate, acquire application locks, or run managed code.
The callback context remains valid only during `Capture`.
Failures remain distinct from successful walks without selected frames.

`SnapshotBackend.Windows.cpp` implements the boundary with CLR `DoStackSnapshot`.
A port supplies `CreateSnapshotBackend` in a different translation unit.
A backend must reject unsupported capture operations explicitly.
Do not assume that another operating system supports the same CLR suspension path.
The shared loop samples elapsed time. CPU-time sampling requires further platform work.
The factory runs on the target thread. A backend binds its CPU clock to that thread.
`ReadCpuNanoseconds` returns cumulative kernel plus user CPU time in nanoseconds.
It returns an error when the clock is unavailable. Values must not decrease within a session.
The Windows backend duplicates the current thread handle and closes the duplicate on destruction.
It reads `GetThreadTimes`, whose values use 100-nanosecond units.
Those units do not establish clock resolution. The retained runs have a maximum CPU delta of 15.625 milliseconds.
The sampler records CPU observations separately from stack samples and does not create delay credits from them.
The profiler caches the IL-to-native map in `JITCompilationFinished`.
Calling that mapping API from the managed workload's P/Invoke entry is not permitted.
The sampler resolves selected-frame addresses against the cached native ranges.
Multiple native code regions and recompilation during sampling are unsupported.
The controlled workload checks the mapped original IL against portable PDB sequence points.
Selected stack frames can be callers. They do not establish exclusive CPU attribution.

`Exports.h` selects DLL exports or ELF/Mach-O symbol visibility.
Module selection accepts both path separators.
`Build.ps1` downloads Windows CLR headers and uses MSVC.
`BuildLinux.ps1` uses pinned runtime sources, PAL compatibility headers, Clang, CMake, and `minipal` in a Linux container.
Both scripts use `GenerateCallbacks.ps1` to check and generate the CLR callback base.
Shared source uses platform-correct UTF-16 literals and comparisons.
Standard C++ headers precede PAL headers because PAL annotation macros conflict with libstdc++ declarations.
`SnapshotBackend.Unsupported.cpp` returns `E_NOTIMPL` on Linux. It does not simulate stack capture.
The Windows `.def` file belongs only to the Windows build.
`Verify.ps1` uses the Windows DLL path and loader search path.
`VerifyLinuxNative.ps1` uses the ELF library path and `LD_LIBRARY_PATH` inside bounded Linux containers.
The Linux runner verifies baseline, profiled, known-credit intervention, and explicit sampling rejection.
These containers have no network or capabilities and have read-only source mounts.
The host-side scripts require PowerShell 7, Git, and Docker. Only a Windows host has been verified.
The Linux toolchain image build requires network access for Ubuntu compiler packages on its first run.
The build obtains the verification SDK image when Docker does not have it locally.
The runtime checkout must match the pinned commit and contain no local changes.

```powershell
dotnet run --project build/build.fsproj -- -t NativeVerify
dotnet run --project build/build.fsproj -- -t NativeLinuxVerify
dotnet run --project build/build.fsproj -- -t NativeCountdownVerify
dotnet run --project build/build.fsproj -- -t NativeLinuxCountdownVerify
```

The `sampled` workload runs for at least 500 milliseconds within a 30-second process limit.
It requires successful attribution to the selected method and preserves the arithmetic result.
It reports all attempted walks and failures.
The target thread must remain alive until sampling stops.
Thread destruction and profiler shutdown stop the worker before releasing backend state.
General concurrent lifecycle use remains unverified.
The workload restarts sampling for a 500-millisecond sleep phase.
It requires unchanged CPU observations, less CPU time than the busy phase, and successful stack walks without `Work` frames.
The separate `intervention` process connects an injected loop probe to the managed delay ledger.
It checks known credits of zero and 20 milliseconds at probe 10,000 for dense probes.
Countdown mode uses probe 10 inside the same long call.
Both cases must preserve the checksum and exact probe count. The positive case must incur the requested wait.
This verifies the intervention path. It does not turn sampled CPU observations into credits.

See [runtime evidence](../reliability/features/runtime-probes/README.md) and [third-party notices](THIRD-PARTY-NOTICES.md).
