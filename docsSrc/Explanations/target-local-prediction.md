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
| No waits before the final join, or waits through `TargetBarrier` | Locks, `Monitor`, `System.Threading.Barrier`, I/O, and any other blocking |
| Tiered compilation and ReadyToRun disabled, checked by `CodeBinding` | Tiered code replacement, OSR, and ReadyToRun images, which `runBound` rejects. ReJIT, which `runBound` detects and fails. A test profiler shows this. |
| Selected code as named method ranges from the perf map | Source lines, inlined callees, and code that the JIT moves |
| Workloads with little allocation | GC-heavy workloads. Five current-design forced-GC checks each broke the 2 ms debt bound in 1 of 10 runs. |
| One profiled thread for each physical core, on hosts with spare CPUs | Loaded hosts. A runnable thread that waits in the kernel run queue adds wall time that the profiler cannot remove. One barrier run had blocks off by 11 points with 17 to 81 ms of run-queue delay. Another broke the 2 ms debt bound with no forced GC. |

## Why a woken thread skips only to the release token

A thread that waits for another thread must not owe the delay that its waker inserted during the wait. Coz therefore lets a woken thread skip that delay.
Pinned Coz skips to the global delay at wake time. Credit that a third thread publishes after the release, but before the woken thread runs, is then skipped too.
CozNet skips only to the token that the releaser posted after it caught up. Credit after the post is paid, not skipped.
A timeout, a cancellation, or a spurious wake skips nothing, because no other thread released the waiter.
The [barrier qualification](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/target-local/barrier.md) shows the effect: in `selected-dominant`, the wakes skip about half of the global delay.
The handler pays delay with a spin at managed instruction addresses inside the work methods. Other addresses defer payment.
Forced-GC checks run ten profiled runs each, with a blocking gen-2 GC every 5 ms of work. No run crashes or loses samples.
Five current-design checks each fail 1 of 10 runs by the 2 ms debt bound. An earlier check with a flawed collection count also failed 1 of 10. In one failure, the peer is 1.65 ms behind when the selected thread publishes 0.5 ms. In another, the peer has no event for 6.5 ms before the failure and 5.3 ms of run-queue delay outside its start. Neither journal shows why the peer fell behind. The check now records a window around each forced collection. In the latest failure, the first with windows, the peer has no event for 5.24 ms and 9.14 ms of whole-run run-queue delay. The windows cover only 0.22 ms of its 5.75 ms failure interval, and the other worker keeps its 1 ms sample rate. The others pass 10 of 10.
One barrier run with no forced GC broke the same bound, so forced GC is not the only trigger. See [the transition evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/target-local/transitions.md).
`runBound` rejects tiered compilation before the run and detects a moved code range after it. A checked run with tiering on rejects both ways.
`Environment.Exit` during the profiled work exits with code 0. The check does not show that a handler was inside a payment spin at the exit. A ReJIT of a bound method during the run fails the run with `BindingError`. Thread abort is not checked.

## How SMT and the run queue can bias the prediction

A thread that pays delay spins. On a core shared with another profiled thread through SMT, the spin changes how much the two threads slow each other.
In one `peer-bound` barrier run, every block predicted a small gain that did not exist.
A diagnostic run with one thread per physical core showed no sign bias. It also halved the CPU count, and a later unrestricted run showed no bias either, so SMT is a hypothesis.
A runnable thread that waits for a CPU also cannot pay. Its debt stays, so a debt failure stays a failure. Each barrier wait now records its own run-queue delay to place such a failure. In the diagnostic run, a woken peer started 8.7 ms after its release and broke the 2 ms debt bound.
Its whole-run run-queue delay was 8.6 ms, which is consistent with that wait but does not place it.
Each worker also records its start run-queue delay. In an earlier prediction run, a peer started work 5.3 ms after the selected thread with 8.3 microseconds of start run-queue delay, and its first sample broke the debt bound. So that late start has a cause other than run-queue delay, which is not yet found. A start trace showed about 0.7 ms of profiler setup per worker after sampling starts. Each worker now does that setup before sampling starts, and the median setup segment is 0.0049 ms. CLR safety outside the qualified workload is not established.

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
| Profile-zero overhead | Not isolated. Probe cost was the largest measured stage. | Handler share about 0.1% of elapsed time. Elapsed ratio medians from about 0.96 to 1.04. |
| Runtime safety | Managed controller with native requests. Controller preemption broke the debt bound. | Native handler at managed addresses. Five current-design forced-GC checks each fail 1 of 10 runs. One barrier run without forced GC also broke the debt bound. |
| Prediction error | About 10 to 30 points of shortfall | Median absolute difference about 1 to 3 points in the qualified model |

The comparison does not use the same workload. The target-local path removes the managed controller from each sample and payment.
The earlier comparisons left component attribution unresolved, so this comparison does not show which change reduced the error.
IL rewriting remains the only CozNet path for code that the handler cannot pay at, such as code outside the eligible ranges.

See [the tutorial](../Tutorials/Target_Local_Prediction.md) and [the API reference](../Reference/target-local-api.md).
