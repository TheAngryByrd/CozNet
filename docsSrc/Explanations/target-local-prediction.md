# Target-local prediction and its support boundary

## Why the fork/join model gives an exact answer

Coz does not speed up the selected code. When the selected code runs, every other thread waits instead.
Each selected sample of period P at virtual speedup s adds `P * s` to a global delay.
Each other thread pays that delay before it continues. The program then runs as if the selected code were faster, plus the delay.
Coz subtracts the delay from the measured time. See [the Coz paper](https://arxiv.org/abs/1608.03676).

Take a selected thread with work A and a peer thread with work B. The program ends when both threads finish.
The selected thread inserts delay D, about `s * A`. The peer pays D, so it finishes at `B + D`.
The effective duration is `max(A, B + D) - D`, which equals `max(A - D, B)`.
A real optimization gives `max(A * (1 - s), B)`. The two values agree.

If B is larger than A, the peer is the critical thread. Both values equal B, so the profiler predicts no gain.
This is the result that a conventional CPU profile cannot give. It shows the same selected CPU time in both cases.

## Why each thread pays its own delay

Earlier CozNet backends used a managed controller to read samples and request payment.
When the operating system preempted that controller, samples queued up and credit arrived in bursts. Debt then passed the 2 ms bound.
In the target-local profiler, each sampled thread reads its own samples and pays its own debt in its signal handler.
No central thread runs for each sample. See [the dispatch explanation](target-local-sample-processing.md).

## Why the replay runs in managed code

The native ledger is the only accounting owner while the program runs.
After the run, managed code reads every journal once and checks it against the ledger rules.
A mismatch rejects the run. The replay never changes credit. This gives one owner and one exact reconciliation.

## Support boundary

The qualified model is narrow. It is a model of what CozNet can predict today, not a general .NET profiler.

| Supported | Not supported |
| --- | --- |
| Linux x64, kernel with `PERF_FORMAT_LOST` | Windows, macOS, Linux ARM64 |
| CPU-bound managed threads that register through the CozNet ABI | Threads that do not register, the thread pool, tasks, and async |
| No waits before the final join | Locks, `Monitor`, `Barrier`, I/O, and any other blocking |
| Tiered compilation and ReadyToRun disabled | Tiered code replacement and ReadyToRun images |
| Selected code as named method ranges from the perf map | Source lines, inlined callees, and code that the JIT moves |
| Workloads with little allocation | GC-heavy workloads. One earlier forced-GC check broke the 2 ms debt bound in 1 of 10 runs. |

A blocked thread needs the Coz wake rule, so that it skips delay inserted while it waited. The native ledger does not implement that rule yet.
The handler pays delay with a spin at managed instruction addresses inside the work methods. Other addresses defer payment.
A forced-GC check runs ten profiled runs with a blocking gen-2 GC every 5 ms of work. All ten pass with no crash, loss, or ledger failure.
An earlier check with a flawed collection count failed the 2 ms debt bound in 1 of 10 runs, for an unknown reason. See [the transition evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/target-local/transitions.md).
JIT, tiering, ReJIT, thread abort, and process exit during payment are not checked. CLR safety outside the qualified workload is not established.

## IL rewriting compared with native intervention

CozNet first paid delay at injected IL probes. Those backends kept a managed controller for each sample and each payment.
Their retained four-arm comparisons underpredicted real optimizations by about 10 to 30 percentage points.
In the [component comparison](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/linux-perf/component-overhead.md), injected probes were the largest measured overhead stage in a small loop.
Their instructions also stayed inside selected-method CPU attribution.
See [the selected CPU comparison](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/selected-cpu.md)
and [the callback-reuse comparison](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/receipt-callback-reuse.md).

| Measure | IL probes with a managed controller | Target-local native handler |
| --- | --- | --- |
| Coverage | Probe sites inside rewritten methods | Any sampled instruction. Payment needs an eligible address. |
| Profile-zero overhead | Not isolated. Probe cost was the largest measured stage. | Median ratio 0.98 to 1.02, inside run-to-run variation |
| Runtime safety | Managed controller with native requests. Controller preemption broke the debt bound. | Native handler at managed addresses. Forced GC passes 10 of 10. An earlier check failed 1 of 10. |
| Prediction error | About 10 to 30 points of shortfall | Median absolute difference about 1 to 3 points in the qualified model |

The comparison does not use the same workload. The target-local path removes the managed controller from each sample and payment.
The earlier comparisons left component attribution unresolved, so this comparison does not show which change reduced the error.
IL rewriting remains the only CozNet path for code that the handler cannot pay at, such as code outside the eligible ranges.

See [the tutorial](../Tutorials/Target_Local_Prediction.md) and [the API reference](../Reference/target-local-api.md).
