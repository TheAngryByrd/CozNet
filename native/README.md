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

See [runtime evidence](../reliability/features/runtime-probes/README.md) and [third-party notices](THIRD-PARTY-NOTICES.md).
