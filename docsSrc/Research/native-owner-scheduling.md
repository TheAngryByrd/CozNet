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

## Primary sources

[1] [Native Coz sampler setup](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L598-L623).

[2] [Native Coz delay and sample processing](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L678-L758).

[3] [JCoz signal dispatch](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L156-L165) and [experiment sender loop](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L198-L220).

[4] [JCoz target handler accounting](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L642-L666).

[5] [WSL perf switch marker](https://github.com/microsoft/WSL2-Linux-Kernel/blob/linux-msft-wsl-5.15.153.1/kernel/events/core.c#L8958-L8963).

[6] [WSL yield path](https://github.com/microsoft/WSL2-Linux-Kernel/blob/linux-msft-wsl-5.15.153.1/kernel/sched/core.c#L8217-L8245).

[7] [Native Coz experiment result handling](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L359-L477).

[8] [JCoz experiment completion and result handling](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L198-L257).
