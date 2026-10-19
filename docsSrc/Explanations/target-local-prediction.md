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

## Why a causal profile is not a straight line

A causal profile shows the program gain for each candidate at several virtual speedups. `TargetCausalProfile.run` measures each point against a baseline in the same block.
Take a selected thread with 6000 rounds and a peer with 3000. At 25%, the selected thread is still the critical thread, so the program gains 25%.
At 50%, both threads finish together, so the program gains 50%. At 75%, the peer sets the end time, so the gain stays at 50%.
A faster peer gives no gain at any speedup, because the peer is not on the critical path. A CPU profile shows neither the plateau nor the zero line. In the first qualification run, real optimizations confirm the predicted profile at all six points, within 3.6 points at the median.

A throughput progress point counts units of work. The profile then predicts the change in the effective time for each unit.
For two independent workers, a faster worker adds its extra units to the total. So the gain grows with the speedup, but less than for one worker alone.
The effective time removes the inserted delay, so a worker that pays delay keeps its rate in the prediction.
In the first throughput qualification run, real optimizations confirm the predicted gains at all six points, within 3.1 points at the median.

A pipeline connects a producer and a consumer through a `TargetQueue`. The slower stage sets the rate, so a faster producer gains only until the consumer is the slower stage.
A lock works in the same way. When work inside a `TargetLock` sets the rate, a faster critical section gains, and faster work outside it gains nothing until the lock is no longer the bottleneck.
In the first lock qualification run, the `Work.selected` prediction is about 9 points below the real gain at the knee, where the critical section equals the work outside it. Its other speedups pass. `peer-25` also fails, by one block of 11.87 points. In the second lock run, the time from a release to the acquire of a waiting thread is 7.40% of the time in the profile at the knee and 2.18% in the real optimization. This is consistent with the knee error, but it does not show the cause. The seventh lock run passes all six points. Its `selected-50` blocks are still below the real gain, by 2.90 to 8.23 points, so the knee error stays but falls within the limits in that run. The eighth lock run fails `selected-50` again, with every block below the real gain.
A `TargetLock` can spin up to 1 ms before it blocks. In the first spin lock run, the share at the knee is 2.84% in the profile, and the knee prediction is within 3 points in three valid blocks. Two blocks are invalid because they have too few visits. At `selected-75`, the spin lock prediction is above the real gain in all three valid blocks, by a median of 6.87 points.
Two workers that take turns through `TargetSemaphore` objects run no work in parallel, so each part gains in proportion to its share of the period. In the first semaphore run, four of six points pass, and both lines follow this model. `selected-75` fails by one block of -10.61 points, and `peer-75` by a median of 5.24 points. The second semaphore run passes all six points. The third fails `selected-50` and `selected-75` by their medians, and all ten of those blocks are below the real gain.
A consumer that waits for an item does not run, so it cannot pay delay. When it wakes, it skips the delay up to the token of the item that it takes, as at a barrier.
In all twelve pipeline qualification runs, real optimizations confirm the producer plateau. One consumer point fails its limit in each of the first three runs, in the sixth, and in the eighth, where `peer-25` has one block of 10.11 points. The fourth, fifth, seventh, ninth, eleventh, and twelfth runs pass all six points. The tenth fails by one invalid block, after a consumer records its wake 3.06 ms after the post of its token, and `peer-50` fails by one block of -11.12 points.
At `peer` 50%, the median prediction is below the median actual gain in the first six runs. In the seventh, it is 0.43% against -0.40%, in the eighth, 1.54% against -0.85%, in the ninth, -1.29% against 0.38%, in the tenth, 0.05% against 0.12%, in the eleventh, 0.21% against -0.11%, and in the twelfth, 0.27% against 0.59%. In the third run, two blocks are above their actual gains by 8.99 and 8.42 points. In the fourth, two blocks are above by 4.03 and 1.98 points. In the fifth, one block is above by 0.75 points. In the sixth, one block is above by 10.02 points. So the error does not have one stable sign. In the second run, which records thread data, the prediction tracks the producer time outside measured payments, for each visit. The records do not show what that time is.

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
| Workloads with little allocation | GC-heavy workloads. Eight current-design forced-GC checks each broke the 2 ms debt bound in 1 of 10 runs. |
| One profiled thread for each physical core, on hosts with spare CPUs | Loaded hosts. A runnable thread that waits in the kernel run queue adds wall time that the profiler cannot remove. One barrier run had blocks off by 11 points with 17 to 81 ms of run-queue delay. Another broke the 2 ms debt bound with no forced GC. |

## Why a woken thread skips only to the release token

A thread that waits for another thread must not owe the delay that its waker inserted during the wait. Coz therefore lets a woken thread skip that delay.
Pinned Coz skips to the global delay at wake time. Credit that a third thread publishes after the release, but before the woken thread runs, is then skipped too.
CozNet skips only to the token that the releaser posted after it caught up. Credit after the post is paid, not skipped.
A timeout, a cancellation, or a spurious wake skips nothing, because no other thread released the waiter.
The [barrier qualification](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/target-local/barrier.md) shows the effect: in `selected-dominant`, the wakes skip about half of the global delay.
The handler pays delay with a spin at managed instruction addresses inside the work methods. Other addresses defer payment.
Forced-GC checks run ten profiled runs each, with a blocking gen-2 GC every 5 ms of work. No run crashes or loses samples.
Eight current-design checks each fail 1 of 10 runs by the 2 ms debt bound. An earlier check with a flawed collection count also failed 1 of 10. In one failure, the peer is 1.65 ms behind when the selected thread publishes 0.5 ms. In another, the peer has no event for 6.5 ms before the failure and 5.3 ms of run-queue delay outside its start. Neither journal shows why the peer fell behind. The check now records a window around each forced collection. In the first failure with windows, the peer has no event for 5.24 ms and 9.14 ms of whole-run run-queue delay. The windows cover only 0.22 ms of its 5.75 ms failure interval, and the other worker keeps its 1 ms sample rate. In the latest failure, one window covers 1.31 ms of the 5.26 ms failure interval. The others pass 10 of 10.
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
| Runtime safety | Managed controller with native requests. Controller preemption broke the debt bound. | Native handler at managed addresses. Eight current-design forced-GC checks each fail 1 of 10 runs. One barrier run without forced GC also broke the debt bound. |
| Prediction error | About 10 to 30 points of shortfall | Median absolute difference about 1 to 3 points in the qualified model |

The comparison does not use the same workload. The target-local path removes the managed controller from each sample and payment.
The earlier comparisons left component attribution unresolved, so this comparison does not show which change reduced the error.
IL rewriting remains the only CozNet path for code that the handler cannot pay at, such as code outside the eligible ranges.

See [the tutorial](../Tutorials/Target_Local_Prediction.md) and [the API reference](../Reference/target-local-api.md).
