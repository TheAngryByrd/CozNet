# Native owner scheduling and local debt payment

This explanation compares the service dependency in CozNet with native Coz and JCoz.
It recommends a controlled scheduling comparison. It does not establish a scheduling fix or CLR signal safety.

## Retained evidence

The [owner capture](../../reliability/features/native-signal/service-submission/owner-scheduler.md) reports a failed treatment with valid scheduler integrity.
For peer command 19, child 8, the last handler clock reading preceded native owner observation by 6,239,753 ns.
The owner was switched out for 6,236,648 ns of that interval.
The controller and target each had zero observed switch-out overlap in that interval.
The owner interval has `preempt: true`.

These records locate an observation delay while the owner was not executing.
They do not identify the scheduler decision, prove that `yield` caused the interval, or exclude diagnostic overhead.
The preemption marker alone does not identify the call site or reason for the switch.
The observation also does not establish the delay distribution across other workloads or hosts.

In the referenced WSL kernel source, perf sets this marker when the outgoing task remains on its run queue. [5]
The `sched_yield` path also calls the scheduler without first making the task sleep. [6]
Thus, the marker does not exclude a yield path.
These source paths do not prove the identity of the running kernel binary or the cause of this recorded switch.

The current [owner loop](../../native/service/ServiceOwner.cpp) needs a matching acknowledgment before it sends the next bounded child.
During active service, the loop releases its mutex, calls `std::this_thread::yield()`, and acquires the mutex again.
A peer can therefore resume application work while the owner has not yet observed its completed wait.
The next child depends on the owner executing again.
The current evidence makes this dependency relevant. It does not prove that the mutex or yield caused the failure.

## What upstream implementations do

| Mechanism | Linux native Coz | JCoz | Current CozNet service |
| --- | --- | --- | --- |
| Sample delivery | Per-thread task-clock sampling and processing timer [1] | Central thread sends `SIGPROF` to registered threads [3] | Controller processes independent CPU samples |
| Debt comparison | Target compares global and local delay after sample processing [2] | Target compares counters every ten signals [4] | Managed accounting prepares finite native commands |
| Debt payment | Target waits for the current full difference [2] | Target sleeps for the current full difference [4] | Owner sends children of at most 50 µs |
| Next payment dependency | No owner acknowledgment between wait fragments in `add_delays` [2] | No owner acknowledgment between wait fragments in `Handle` [4] | Owner must observe each child before another child |

Native Coz credits selected samples before it checks debt.
On Linux, it stops sampling during the wait and credits the measured wait to the target's local counter. [2]
JCoz also credits the measured sleep locally. Its central sender remains a dependency for future sampling signals. [3][4]
Thus, JCoz is not independent of a dispatcher.
Its dispatcher does not authorize each 50 µs fragment of a larger debt payment.

The upstream paths do not implement CozNet's bounded child protocol.
Their full-debt waits cannot replace that protocol without a new intervention contract.
Neither source establishes that its handler operations are safe inside the CLR.
The [implementation comparison](managed-coz-implementations.md) describes JCoz's separate synchronization and runtime limits.

## Meaning of the unchanged bounds

| Bound | Design consequence |
| --- | --- |
| 50 µs requested wait per child | A larger peer debt needs several independently bounded children. |
| 5 ms measured handler duration | A bounded request still fails if execution or descheduling makes the measured handler too long. |
| 2 ms observed debt | A scheduling gap can exceed service capacity before acknowledgment times out. |
| 100 ms acknowledgment through first managed receipt | Native observation alone does not complete receipt validation. |

These bounds address different failures.
A request can satisfy the handler and acknowledgment limits while the experiment fails the debt limit.
The observed owner's off-CPU interval does not justify forgiving debt or crediting an unperformed wait.

The inspected native Coz and JCoz accounting and result paths have no equivalent absolute per-target unpaid-debt cap. [2][4][7][8]
Both request the full current debt difference. Their cadence and full-debt waits therefore cannot directly satisfy CozNet's bounded protocol.
Native Coz separately rejects results when accumulated global delay reaches elapsed duration or progress is insufficient. [7]
Those result checks do not establish a maximum outstanding debt for each target.

Ordinary preemption before a measured wait does not earn local delay credit in either inspected handler. [2][4]
Later settlement does not prove that the earlier progress measurements match a physical optimization.
CozNet retains the 2 ms debt gate and the separate two-percentage-point prediction gate.
The [sampled timer capture](../../reliability/features/native-signal/timer-sampled.md#first-scheduler-capture) establishes peer preemption during one failed delivery interval.
Its [observer comparison](../../reliability/features/native-signal/timer-sampled.md#scheduler-capture-throughput-comparison) does not establish a small diagnostic-overhead bound.

## Bounded polling result

The implemented prototype compares the existing yield policy with a finite active-poll policy behind a portable owner scheduling interface.
The owner polls for 64 active iterations, then yields.
It must release the shared mutex between polls so submissions, snapshots, and cancellation remain available.
Idle service retains its condition-variable wait.
OS-specific scheduling operations remain behind the backend boundary.

This is a falsifiable comparison, not a claimed fix.
It tests whether avoiding an unconditional yield at each active iteration reduces acknowledgment gaps on the controlled workload.
It must retain all timing gates, failures, CPU costs, and owner scheduler records.
Real-time priority and CPU affinity are not prerequisites for this comparison.
Active polling can increase CPU use and can still lose execution time to the scheduler.

The [first controlled comparison](../../reliability/features/native-signal/service-submission/owner-polling.md)
fails the 2 ms debt limit with both policies during warmup.
The bounded policy retains a 10.54 ms interval from the last handler clock reading to owner observation.
Almost all of that interval overlaps an owner switch-out.
The experiment does not establish that bounded polling improves capacity or CPU cost.
The default remains yield after every active iteration.

## Next design question

A later prototype could move finite lease consumption into target-local intervention state.
That design would remove owner execution from the path between bounded child waits.
It would still need fresh eligibility checks, cancellation, exact-once journals, measured overshoot, and target lifetime protection.
Repeating 50 µs waits inside one handler would not establish compliance with the 5 ms handler limit.
Returning between children would need a verified mechanism for the next intervention.
The controller must also retain ownership until valid terminal accounting and first managed receipt.

Cooperative IL probes offer another target-local check point.
They avoid arbitrary signal intervention at that point but require adequate coverage and acceptable measurement distortion.
The [IL rewriting decision](coz-port-and-il-rewriting.md) describes those limits.
Neither local settlement nor bounded polling establishes physical prediction accuracy.

## Cooperative waits under CPU contention

A separate native diagnostic calls the actual `CooperativeSpinTiming.wait` implementation without the CLR.
It requests 50 microseconds per call and pins the target to one available guest CPU.
Four blocks compare the target alone with two competing threads on that same CPU.
Each case records 10,000 measured calls after 1,000 warmup calls.

All eight cases passed the diagnostic clock and record checks.
The contended cases retained 204 waits above 5 ms across 40,000 calls.
Every long observation bracket also recorded an involuntary context switch.
The four cases alone retained no wait above 5 ms.
The largest contended wait was 43.558 ms, while the largest wait alone was 0.555 ms.

The median thread-CPU delta among long waits was 55 to 67 microseconds within each contended case.
The full retained range was 24.2 microseconds to 10.265 ms.
CPU and context-switch readings bracket the wait and include observation work.
They do not locate a context switch inside the exact wait interval or establish calibrated clock accuracy.

The seeded order placed contention first in every block, so order and time drift remain confounded.
The diagnostic ran on Linux x64 under WSL2. It does not establish another platform's scheduling behavior.
The non-Linux diagnostic branch reports an unsupported CPU/affinity backend.

These records show that the native wait can exceed the service limit without CLR execution.
They do not explain the earlier managed failure, which lacks matching thread-CPU and scheduler observations.
A completed spin-wait call also does not imply a valid service child: the lease applies the separate 5 ms limit.
No limit or payment clock changes follow from this diagnostic.

The next live diagnostic needs bounded per-child CPU observations tied to the existing request identity.
It must preserve unsupported capability, read failures, overflow, and the original service failure.
See [the native wait evidence](../../reliability/features/native-signal/cooperative-wait-cpu.md).

## Primary sources

[1] [Native Coz sampler setup](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L598-L623).

[2] [Native Coz delay and sample processing](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L678-L758).

[3] [JCoz signal dispatch](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L156-L165) and [experiment sender loop](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L198-L220).

[4] [JCoz target handler accounting](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L642-L666).

[5] [WSL perf switch marker](https://github.com/microsoft/WSL2-Linux-Kernel/blob/linux-msft-wsl-5.15.153.1/kernel/events/core.c#L8958-L8963).

[6] [WSL yield path](https://github.com/microsoft/WSL2-Linux-Kernel/blob/linux-msft-wsl-5.15.153.1/kernel/sched/core.c#L8217-L8245).

[7] [Native Coz experiment result handling](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L359-L477).

[8] [JCoz experiment completion and result handling](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L198-L257).


## Owner CPU observations in the managed path

The optional child CPU capture now runs in the native cooperative path.
It retains failed waits without changing wall-time payment or service bounds.
The first fixed-work managed capture completed 10,000 treatment batches.
It retained 274 peer waits and 24 selected waits with no CPU read failure or clock-consistency rejection.
The longest peer wait was 68.191 microseconds. The longest selected wait was 0.401 microseconds.
This capture did not reproduce the earlier millisecond overrun.
It does not establish sustained capacity or identify the cause of that failure.

See [the retained contract and capture evidence](../../reliability/features/native-signal/cooperative-child-cpu.md)
and [the capture procedure](../How_Tos/run-bound-selected-payment.md#capture-child-cpu-observations).


A second capture used the original 50,000-batch workload length with CPU diagnostics enabled.
Treatment completed with 1,469 peer waits and 108 selected waits.
The longest handler interval was 234,747 ns, below the unchanged 5 ms limit.
All three cases passed replay. The earlier failure remains unexplained.


## Constrained managed scheduling

A separate capture restricts the unchanged 50,000-batch workload to guest CPU 0.
The baseline can use guest CPUs 0 through 3.
This restriction also changes processor availability for runtime policy.

Treatment fails after 8,538 selected batches with 2,071,535 ns of unpaid peer debt.
Replay verifies the records and preserves the failure.
The zero-speedup and expected-invalidation cases pass.
No retained handler exceeds 5 ms.

One 50,000 ns peer request measures 939,001 ns of wall wait and 35,700 ns of CPU advance.
A later request has no child at a native snapshot 11,365,728 ns after arming.
Its first child starts 13,098,192 ns after arming and completes during failed-run cleanup.
These observations separate wall/CPU divergence from delayed child service.
They do not identify the scheduler decision or explain the earlier selected-handler overrun.

The [single-CPU evidence](../../reliability/features/native-signal/cooperative-single-cpu.md) retains the limits and exact records.
The next question is how buffered credit publication and target service availability combine at the debt boundary.
The current absolute debt gate still differs from the inspected upstream Coz paths.


The final four credited samples add 2,000,000 ns to the pending 71,535 ns budget.
Their source timestamps precede native request arming by approximately 0.98 to 3.98 ms.
Both source and native timing code select `CLOCK_MONOTONIC`.
This ordering uses the configured clock contract, not an independent calibration.
The capture lacks individual drain and publication timestamps, so it cannot assign the backlog to one component.

A useful next experiment is owner-driven sample draining before debt checks.
The inspected native Coz path processes target samples before checking delay debt. [2]
An owner-driven mode could remove one collector scheduling dependency.
It must preserve sample integrity and payment exclusion, measure probe cost, and retain the existing failure gates.
No implementation or capacity improvement is claimed for that proposed mode.

## First owner-driven drain comparison

The opt-in owner collector removes the background publication step from selected sample processing.
It drains the ring before selected-probe sample counts and debt checks.
The source still stops during selected payment. All debt, child-wait, handler, and receipt bounds remain unchanged.
Owner drain CPU runs on the sampled thread while its source is enabled.
That observer work can enter selected sample periods even though the credit rule remains unchanged.

Both captures used qualified-sample cadence, child CPU observations, and 10,000 requested selected batches.
They used the ordinary container CPU set. They did not repeat the single-CPU restriction.

| Collection | Zero-speedup elapsed | Treatment result | Invalidation |
| --- | ---: | --- | --- |
| Background | 88.097 ms | Failed after 2,390 batches at 3,385,057 ns selected debt | Passed |
| Owner | 109.662 ms | Completed 10,000 batches in 197.249 ms | Passed |

Background evidence: `artifacts/owner-drain-background/20261015T210451753-63dd4ae6`.
Owner evidence: `artifacts/owner-drain-owner/20261015T210705271-a5d31f30`.
Both captures retained unchanged source and binary hashes during execution.
Replay verified all six records, including the failed background treatment.

An earlier owner activation attempt failed before any selected batch because the workload still selected the background collector.
The ownership guard rejected all three cases.
That failed capture remains at `artifacts/owner-drain-owner/20261015T210542258-aa05b764`.
The corrected workload selects `StartOwnerPausable` when owner draining is enabled.

The zero-speedup owner duration was about 24.5% higher in this pair.
This is one fixed-order observation, not an isolated overhead estimate or a stable capacity result.
The failed background treatment has no completed elapsed endpoint for a paired treatment comparison.
The captures do not establish prediction accuracy or resolve the earlier single-CPU failure.

See [the collector contract](../Reference/pausable-sample-collector.md#owner-driven-collection)
and [the comparison procedure](../How_Tos/run-bound-selected-cadence.md#compare-owner-driven-collection).

A final capture includes the cleanup-reentry guard:
`artifacts/owner-drain-final/20261015T211653127-b8a9d532`.
All three cases pass, and replay verifies their records.
The zero-speedup and treatment cases each complete 10,000 selected batches.
Source and binary hashes remain unchanged during this capture.
This verifies the final guarded path. It does not establish stable capacity or prediction accuracy.

The subsequent [single-CPU comparison](../Explanations/owner-drain-capacity.md) fails in both collection modes.
Owner draining does not remove the observed debt and receipt capacity failures.
