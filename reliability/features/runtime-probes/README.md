# Runtime probe evidence

The Windows x64 prototype preserves arithmetic results and reaches probes inside a long managed loop.
This check does not establish causal prediction accuracy or acceptable probe overhead.

Source baseline: `c146561`, with the uncommitted native profiler and instrumentation workload.
CPU-clock extension baseline: `65bb7ad`, with local changes to the backend, sampler, and workload.
Runtime: .NET 10.0.12, x64. SDK: 10.0.112. Configuration: Release.
Tiered compilation and ReadyToRun are disabled for both child processes.
Each process has a 30-second limit. Inputs are fixed. No random seed applies.

```powershell
./native/Build.ps1
dotnet build tests/CozNet.Instrumentation -c Release
./native/Verify.ps1
```

| Iterations | Expected sum | Baseline probes | Instrumented probes |
| --- | --- | --- | --- |
| 0 | 0 | 0 | 2 |
| 1 | 0 | 0 | 3 |
| 1,000,000 | 499,999,500,000 | 0 | 1,000,002 |

The compiler emits `blt.s` at IL offset 18, targeting offset 7.
Its condition runs `count + 1` times. The entry call adds one probe.
The workload requires the exact probe count and an independently calculated sum.
It also checks one instrumented method, 20 monotonic IL map entries, and rejection of an out-of-range map index.

The first run failed with profiler status 0 and zero probes.
The prototype lacked `ICorProfilerCallback2`, which the CLR requests during activation.
After the interface fix, both child processes completed with exit code 0.
Raw observations are in `data/baseline.json`, `data/profiled.json`, `data/sampled.json`, and `data/intervention.json`.

The sampled run records counts by original IL location and the corresponding portable PDB sequence points.
The raw file records the current call count, walk count, mapping count, and source span.
The initial mapping attempt failed with `CORPROF_E_UNSUPPORTED_CALL_SEQUENCE` (`0x80131363`).
Moving map capture into `JITCompilationFinished` corrected that call sequence.
The workload checks the arithmetic after each call and requires at least one selected-method sample.
Sampling stops before the target thread exits.
This verifies asynchronous stack capture on Windows x64 with the stated runtime settings.
It does not prove CPU-time attribution, sampling frequency, or general lifecycle safety.
The one-millisecond wait request produced approximately 32 attempts in 500 milliseconds.
Do not treat it as a one-kilohertz sampler.

Unsupported cases include exception handlers, switch instructions, tier changes, multiple selected methods, and concurrent probes.
At least one observed offset resolves to the controlled loop in `Program.cs` through its portable PDB.
Counts by location must sum to the mapped sample count.
Entry or return samples can occur after loop samples without invalidating the run.
General source attribution, CPU-time sampling, delay application, and overhead remain unverified.
The CPU-clock extension observes cumulative target-thread kernel plus user CPU time.
It records CPU time, the largest CPU delta, read count, and unchanged observations for busy and blocked phases.
The blocked phase must have a successful stack walk and no selected-method frames.
The busy phase must consume more CPU time than the blocked phase.
Sampling restarts between these phases, which also checks that session counters reset.
The retained runs have a maximum CPU delta of 15.625 milliseconds. They cannot establish one-millisecond sample attribution.
The backend converts the documented 100-nanosecond units to nanoseconds with overflow checks.
See [GetThreadTimes](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-getthreadtimes).
The first CPU-clock run records 484.375 milliseconds of CPU time and 32 mapped samples from 33 attempts.
One walk returns `CORPROF_E_STACKSNAPSHOT_UNMANAGED_CTX` (`0x8013135F`). The failure remains in `data/cpu-clock-first.json`.
That blocked phase records zero CPU time and 33 successful stack walks without selected-method frames.
The current run uses the probe hook and records its own measurements in `data/sampled.json`.

The intervention process converts a synthetic 40-millisecond CPU observation at zero and 50 percent speedup.
This produces known credits of zero and 20 milliseconds in `Stopwatch` ticks.
The controller publishes credit only after the worker reaches injected probe 10,000.
The worker waits inside that probe, then completes the million-iteration checksum with 1,000,002 probes.
The zero-credit case must leave global delay at zero.
The positive case must wait at least the requested duration and record measured overshoot in the ledger.
Both threads settle before unregistering. The recorded intervention delay precedes controller cleanup.
This checks IL injection, live ledger access, and real waiting together. It does not establish causal prediction accuracy.
Selected frames include callers. Mapping one frame does not prove exclusive execution at its source location.
The shared sampler uses standard C++ threading and a platform backend interface.
Only the Windows backend has runtime evidence. See [platform boundaries](../../../native/README.md).
