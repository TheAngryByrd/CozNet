# Bounded Linux signal intervention

The Linux x64 experiment passes three native contract cases and six managed workload cases.
It uses a separate native library without IL rewriting or CLR startup profiling.
These results establish feasibility for one fixed arithmetic method. They do not establish general CLR safety or causal prediction accuracy.
The separate [sampled signal experiment](sampled.md) connects CPU events to requests, but fails its observed unpaid-debt limit.

## Protocol

Command:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxSignalVerify
```

The shared harness runs two dedicated managed threads for approximately two seconds.
Each batch must return `49995000`. The runtime disables tiering and ReadyToRun.
The backend freezes a unique native range from the runtime perf map after warmup.
Each signal carries a unique request sequence. Only an interruption inside that range can request a wait.
Other interruptions produce deferred results without waiting.

The runner shuffles six cases with seed `20261016`:
no signals, zero-delay signals, and 50-microsecond signals, each with and without forced GC.
The baseline still installs the library and registers threads. It sends no signals.
The GC worker allocates one megabyte and requests blocking compacting collections between 20-millisecond waits.
The report records GC call duration and the pause durations reported by `GC.GetGCMemoryInfo()`.
GC call duration is not direct suspension latency.

Bounds set before execution:

| Observation | Limit |
| --- | ---: |
| Requested native delay | 50 microseconds |
| Measured handler interval | 5 milliseconds |
| Request acknowledgment | 100 milliseconds |
| Blocking GC call | 250 milliseconds |
| Child process watchdog | 30 seconds, then forced termination after 2 seconds |

The handler uses a monotonic absolute deadline and does not retry interrupted sleeps.
Scheduling and signal delivery can extend observed duration beyond the requested delay.
The measured handler interval excludes entry checks, result publication, and return.
It cannot prove a bound on the complete signal-handler residence.

## Retained results

[Raw records](data/run-20261014T000044719-11b49f62/summary.json) include all nine outcomes, request acknowledgments, GC observations, binary hashes, source hashes, and native disassembly.
The managed runtime is .NET `10.0.0` in the pinned SDK image.
The runner used a Windows host and Linux x64 Docker containers with the default syscall policy.
PowerShell and Docker are required. Other host operating systems remain unverified.

| Case | Requests | Eligible interruptions | Maximum measured handler interval | GC calls | Maximum GC call |
| --- | ---: | ---: | ---: | ---: | ---: |
| Baseline | 0 | 0 | — | 0 | — |
| Zero delay | 326 | 320 | 0.001673 ms | 0 | — |
| 50 µs delay | 326 | 325 | 0.244100 ms | 0 | — |
| Baseline with GC | 0 | 0 | — | 99 | 0.9979 ms |
| Zero delay with GC | 326 | 325 | 0.002856 ms | 99 | 0.7237 ms |
| 50 µs delay with GC | 326 | 318 | 0.229635 ms | 99 | 0.7417 ms |

Every managed case preserves its checksum and completes thread retirement, join, and signal restoration.
Both target threads receive eligible interruptions in each signal case.
The longest acknowledgment is `4.7695 ms`. No fixed bound fails.
This single shuffled block does not establish a statistical GC or throughput effect.

| Requirement | Evidence |
| --- | --- |
| Preserve an occupied signal | Native `occupied` case rejects installation and preserves `SIG_IGN`. |
| Reject a blocked target | Native `blocked` case rejects registration. |
| Preserve pending ownership | Native `lifetime` case rejects a duplicate request, wrong sequence, and premature restoration. |
| Defer ineligible interruptions | Native `lifetime` case acknowledges two requests without waits. |
| Prevent slot reuse after retirement | Native `lifetime` case rejects a new request after target join. |
| Preserve handler isolation | Compiled handler check finds direct syscalls and no function calls, PLT references, or TLS segment access. |
| Execute real managed work | Six managed cases pass through `LinuxSignalVerify`. |

## Remaining work

The backend assumes a trusted disposable process. Sender metadata detects accidental delivery and does not authenticate hostile code in the same process.
The acknowledgment store does not prove handler return. Static native state and handler code remain loaded for the process lifetime.
An acknowledgment timeout abandons normal teardown. The process watchdog bounds that failure path.
There is no cancellation, slot reuse, general signal chaining, or library-unload contract.

Verify JIT, managed/native transitions, blocked waits, thread exit, and shutdown races before extending the workload scope.
The [transition experiment](transitions.md) records bounded checks for those states and the remaining abnormal-exit limit.
Use runtime traces to measure suspension effects separately from collection-call duration.
Then connect independent CPU samples to bounded delay accounting and repeat physical-optimization prediction checks.
The signal's interrupted instruction pointer is an eligibility check. It is not a CPU sample or causal credit.

The shared harness uses `IBoundedInterventionBackend`. Linux signal operations remain in `LinuxSignalBackend` and `native/signal`.
Windows, macOS, and other architectures need separate backends. The public serial engine remains unchanged.

See [signal research](../../../docsSrc/Research/native-signal-intervention.md), [request lifetime](../../../docsSrc/Research/signal-request-lifetime.md), and [current experiment contracts](../../../lode/profiling/signal-intervention.md).
