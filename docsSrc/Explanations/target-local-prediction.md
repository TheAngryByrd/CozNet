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
| Workloads with little allocation | GC-heavy workloads, which the qualification does not exercise |

A blocked thread needs the Coz wake rule, so that it skips delay inserted while it waited. The native ledger does not implement that rule yet.
The handler pays delay with a spin at managed instruction addresses inside the work methods. Other addresses defer payment.
The runtime-transition checks of earlier signal experiments do not cover this handler. CLR safety outside the qualified workload is not established.

See [the tutorial](../Tutorials/Target_Local_Prediction.md) and [the API reference](../Reference/target-local-api.md).
