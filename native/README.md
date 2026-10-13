# Native profiler prototype

The current build and runtime checks support Windows x64.
Linux and macOS support remain incomplete.

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
`Build.ps1` currently downloads Windows CLR headers and uses MSVC.
A Linux or macOS build needs the matching CLR/PAL headers, compiler configuration, and backend.
The Windows `.def` file belongs only to the Windows build.
The verification runner currently uses a Windows DLL path and loader search path.
These build and launch adapters must change before another platform can pass verification.

```powershell
dotnet run --project build/build.fsproj -- -t NativeVerify
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
It checks known credits of zero and 20 milliseconds after probe 10,000.
Both cases must preserve the checksum and exact probe count. The positive case must incur the requested wait.
This verifies the intervention path. It does not turn sampled CPU observations into credits.

See [runtime evidence](../reliability/features/runtime-probes/README.md) and [third-party notices](THIRD-PARTY-NOTICES.md).
