# Linux CPU-event evidence

The Linux x64 capability workload obtains per-thread CPU events with instruction addresses.
It uses the same Linux event type as native Coz: `PERF_COUNT_SW_TASK_CLOCK`.
It maps addresses to a fixed managed method snapshot. It does not map source lines or generate causal credits.

Source baseline: `ea33664`, plus the live collector and workload checks.
The container uses .NET 10.0.0. Its kernel and image ID are recorded in `data/`.
The requested sampling period is one millisecond of thread CPU time, including user and kernel execution.
Ticks in kernel mode produce no sample. `cpuNanoseconds` includes kernel time.
`representedCpuNanoseconds` sums emitted sample periods. It does not measure selected-method user CPU duration.
The fixed busy and sleep phases each last approximately 500 milliseconds.
The busy phase verifies its checksum against the completed arithmetic batches.
The workload has a 30-second execution limit and no random seed.

```powershell
dotnet run --project build/build.fsproj -- -t LinuxPerfVerify
```

The default Docker syscall policy denies `perf_event_open` with `EPERM`.
The second container disables that filter and successfully collects samples.
Both containers drop all capabilities, disable networking, and use a read-only root filesystem.
The only writable host mount is the selected evidence directory.
This does not require changing the host's `perf_event_paranoid` setting.

The reader checks every sample's user CPU mode, nonzero instruction pointer, process/thread IDs, timestamp order, and positive sample period.
It rejects malformed record boundaries, lost samples, and throttling.
The busy phase must have CPU time and samples. The blocked phase must have less CPU time and fewer samples.
Live counters remain in `data/live-unfiltered.json`. Access denial remains in `data/default.json`.
The earlier fixed-map run remains in `data/unfiltered.json`.
The live run recorded 497 busy-phase samples, including 492 selected-method samples and three unmapped samples.
The target observed all 497 samples before it disabled the event. The final drain found no additional samples.
The selected symbol was `int64 [CozNet.LinuxPerf] CpuWorkload::Run()[Optimized]`.
The other two samples mapped to other methods. The sleep phase recorded zero samples.
No lost events or throttling occurred.
The map was captured after warmup and before sampling. Tiered compilation and ReadyToRun were disabled.
The parser rejects an incomplete snapshot. It does not follow later map updates or code unloads.
The result does not establish general sampling precision, exclusive managed attribution, or safe live intervention.

`CompletionPublishesFinalDrain` checks that completion joins the collector and publishes its final drain.
It blocks the first drain until after the stop request, then requires exactly two drain calls.
It also checks that repeated completion returns the same snapshot.
`FailureReachesCaller` checks that a reader failure reaches the caller without replacing the exception.
Both checks run before Linux sampling. They also passed on Windows through the portable collector mode:

```powershell
dotnet tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll --verify-collector
```

An isolated [mutation check](collector-mutation.md) confirmed that removing the final drain fails this assertion.

Primary API sources:

- [Linux v6.6 perf event ABI](https://github.com/torvalds/linux/blob/v6.6/include/uapi/linux/perf_event.h)
- [Linux v6.6 task-clock accounting and sample filtering](https://github.com/torvalds/linux/blob/v6.6/kernel/events/core.c)
- [Linux x64 syscall table](https://github.com/torvalds/linux/blob/v6.6/arch/x86/entry/syscalls/syscall_64.tbl)
- [Native Coz Linux event configuration](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp)
- [Pinned .NET perf-map implementation](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/perfmap.cpp)

See [the backend plan](../../../lode/profiling/linux-perf.md) and [managed intervention evidence](../runtime-probes/README.md).
