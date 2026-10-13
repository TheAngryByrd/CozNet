# Linux native instrumentation

Source baseline: `8407ad8`, plus the shared native portability changes and Linux build targets in this change.
`NativeLinuxVerify` and `NativeVerify` both passed from a Windows x64 host with SDK 10.0.112.
The Linux container uses .NET 10.0.0. The Windows workload uses .NET 10.0.12.
Each workload process has a 30-second limit. Tiered compilation and ReadyToRun are disabled.

```sh
dotnet run --project build/build.fsproj -- -t NativeLinuxVerify
dotnet run --project build/build.fsproj -- -t NativeVerify
```

The Linux build uses the pinned runtime headers, GUID definitions, and `minipal`.
The shared source uses UTF-16 literals for each platform and avoids `wcscmp`.
Standard library headers precede PAL headers to avoid annotation-macro conflicts.
The Windows build still uses MSVC and its DLL export file.
Both builds use the same checked callback generator.
The targets have separate dependency graphs. The default package target does not build either native profiler.

| Check | Linux x64 | Windows x64 |
| --- | --- | --- |
| Baseline: zero probes | Passed | Passed |
| Instrumented: `count + 2` probes for 0, 1, and 1,000,000 iterations | Passed | Passed |
| Arithmetic checksum and 20 monotonic IL map entries | Passed | Passed |
| Intervention reaches probe 10,000 with zero and 20 ms credits | Passed | Passed |
| Selected stack maps to the loop source span | Unsupported | Passed |
| Unsupported sampling returns `E_NOTIMPL` | Passed | Not applicable |

The Linux positive-credit case reported 22.01 ms inside the managed check and 20.48 ms of global delay.
These values use different measurement boundaries. Both satisfy the known-credit intervention checks.
The Windows sampled case reached the loop source span and passed its busy-versus-sleep and restart checks.
This does not establish acceptable probe overhead or causal prediction accuracy.

Raw integrated evidence is under [linux-integrated](data/linux-integrated/) and [windows-port-regression](data/windows-port-regression/).
The Linux environment record retains the runtime image identity, kernel, and profiler hash.
The earlier isolated build and its source archive remain under [linux-build-experiment](data/linux-build-experiment/).
The [research report](../../../docsSrc/Research/linux-profiler-build.md) records its source constraints and results.

Linux CPU events still use the separate managed `perf_event_open` workload.
The [controlled connection](../linux-perf/injected-probes.md) now joins those events to injected probes, with excessive measured overhead.
macOS, ARM64, and non-Windows build hosts remain unverified.
