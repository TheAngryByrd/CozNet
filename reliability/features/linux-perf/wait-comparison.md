# Wait backend and CPU cadence comparison

Source baseline: `74bbd8c` plus wait backends, CPU cadence, timing observations, and the comparison target.
The four configurations use the same .NET 10.0.0 Linux x64 image and WSL2 kernel recorded with each result.
Each configuration runs the default-policy probe, a 50-percent treatment, and a zero-percent control.
Each process has a 30-second limit. The busy interval is approximately 500 milliseconds before final settlement.

```powershell
dotnet run --project build/build.fsproj -- -t LinuxWaitCompare
```

The initial managed experiment showed a minimum backend wait of about 1.06 milliseconds.
Most requests were shorter than one millisecond. A native timer reduced individual overshoot but did not prevent repeated catch-up waits.
Native Coz processes samples every 10 milliseconds of thread CPU time. The earlier workload checked after every small arithmetic batch.
The current default uses the same CPU interval. Zero selects the previous per-probe mode for comparison.
The ledger's existing Linux overshoot rule remains unchanged.

| Wait backend | CPU interval | Credit | Worker backend calls | Worker accounted pause | Global delay | Worker checks |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Managed | 0 ms | 138.000000 ms | 306.487987 ms | 307.079959 ms | 307.805220 ms | 52,366 |
| Managed | 10 ms | 230.000000 ms | 230.734821 ms | 231.024138 ms | 231.126892 ms | 27 |
| Linux nanosleep | 0 ms | 142.500000 ms | 307.452724 ms | 308.578425 ms | 308.662117 ms | 53,810 |
| Linux nanosleep | 10 ms | 231.500000 ms | 231.284296 ms | 231.561209 ms | 231.638302 ms | 28 |

Each treatment produced live sample application, credit, and worker waits before sampling stopped.
All zero-speedup controls produced zero credit, zero worker wait, and zero global delay.
Every configuration passed the ring, attribution, and loss checks.
The runs used a fixed order and one trial per configuration. They are diagnostic observations, not statistical estimates of prediction accuracy.
The lower selected credit in per-probe mode accompanies more time spent in delay and profiler code.

`data/wait-comparison/` retains each configuration's treatment, control, access-policy result, and environment.
Each wait record has requested ticks, backend-call ticks, and ledger-accounted ticks.
`ClockFrequency` converts these values to time. Samples and wait records remain available for independent aggregation.

The ledger measures a wider interval than the backend-call wrapper.
Scheduling and wrapper work between those boundaries can make backend-call time smaller than credited time.
The final code observes the exact timestamp pair supplied to `CooperativeRuntime` and checks accounted pause against credit.
It still requires positive backend waits during treatment and exact zero waits during the control.
A one-millisecond warmup runs before registration. It is excluded from the experiment counters.

Two earlier batched attempts failed the old backend-only assertion:

- Without delegate warmup: backend waits `222943963`, credit `224500000`, global delay `224700941` ticks.
- With delegate warmup: backend waits `227621782`, credit `228500000`, global delay `228646204` ticks.

Warmup did not resolve the different measurement boundaries. These failures did not indicate unpaid ledger debt.
The current result preserves both measurements instead of adding a timing tolerance.

`CadenceBoundaries` checks before, at, and after the CPU interval and rejects a decreasing clock.
It passed on Windows and Linux. `WaitDurationAndCancellation` passed for both backends on Linux.
The native ABI remains Linux x64 only. `IWaitBackend` and `IThreadCpuClock` provide explicit replacement interfaces.
The nanosleep loop caps individual requests at one millisecond. Scheduler delays can exceed that duration.
Prediction validation, general eligibility, probe overhead, and code lifecycle remain open.

Primary sources:

- [Native Coz sample period and batch size](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.h)
- [Native Coz thread CPU timer](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/ccutil/timer.h)
- [Native Coz delay accounting](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp)
- [glibc 2.39 time declarations](https://github.com/bminor/glibc/blob/glibc-2.39/time/time.h)
- [glibc 2.39 timespec layout](https://github.com/bminor/glibc/blob/glibc-2.39/time/bits/types/struct_timespec.h)

See [the backend contract](../../../lode/profiling/linux-perf.md).
