# Target-local verification status

The qualification runners hash [the prediction plan](target-local-prediction.md), [the transition plan](target-local-transitions.md),
and [the blocking plan](target-local-blocking.md). Those files change only before a run. This file holds the state after the latest runs.

## Wait protocol

The wait protocol is implemented. `TargetBarrier` is the only primitive that uses it.

- `ThreadDelayAccount` has `catchUp`, `post`, `beginBlock`, and `endBlock`. `settle` returns `blocked` while blocked.
- `LinuxTargetSession` has `suspendNotifications`, `resumeNotifications`, and `drainNow` for explicit calls on the session thread.
- The C ABI has `coz_target_post`, `coz_target_wait_begin`, and `coz_target_wait_end`.
- `TargetBarrier` in `src/CozNet/TargetLocalProfiler.fs` is lock-free. The last arriver posts. Other arrivers block, wait on the phase event, and wake with its token.
  An arriver that blocks after the release has a block global at or above the token, so it skips nothing.
- `TargetLocalReplay` checks `block`, `wake`, and `post` events.
- A post does not require local delay to equal global delay, because another thread can publish between catch-up and post.
- The native wake check requires a token no larger than the largest posted token. The replay proves exact match.
- Any worker failure cancels a run-wide token. Barrier waits observe it, so a failed worker cannot leave a peer waiting forever.

```fsharp
let barrier = TargetBarrier 2
let work () = for phase in phases do runPhase phase; barrier.SignalAndWait()
```

Deterministic checks cover dependency, timeout, cancellation, and spurious wakes, debt at block, the post race, and each protocol violation.
A mutation that skips to the wake-time global delay fails the native race check.
The latest [barrier qualification](../../reliability/features/target-local/barrier.md) fails by a 2 ms debt-bound failure at the start of one profiled arm. Five of the eleven barrier runs fail. Of the four earlier failures, one has measured run-queue delay, one has an SMT hypothesis that a diagnostic run is consistent with, one predates the diagnostics, and one followed a diagnostic read inside the timed window that raised overhead.

```mermaid
flowchart LR
    Ledger[Native wait protocol] --> Replay[Managed replay rules]
    Replay --> Barrier[TargetBarrier]
    Barrier --> Qualification[Barrier qualification: latest fail, five failures in eleven runs]
```

## Runtime lifecycle

[The lifecycle plan](target-local-lifecycle.md) adds `CodeBinding` and `runBound`. Every profile mode in the sample uses `runBound`.
Transition run `20261018T203245961-56927b66` passes criteria 9 to 11: tiering on rejects in configuration and capture,
the qualified settings bind and verify, and `Environment.Exit` during the profiled work exits with code 0.
With tiering on, the perf map holds `[QuickJitted]` and `[OptimizedTier1OSR]` entries for each work method.

The run-queue diagnostic read inside the timed window raised the profile-zero overhead ratio to about 1.05 to 1.09.
After the reads moved outside the window, prediction run `20261018T203002656-dab28501` shows ratios of about 0.99 to 1.02.

## Open failures

Prediction run `20261018T182717084-bb88b1d8` and transition run `20261018T182834143-78a15e0b` fail on the tree before the CPU diagnostics.
Transition run `20261018T190118893-b2109c5d` fails 1 of 10 runs on the current tree. Both threads show about 8 to 9 ms of wall time above counted CPU in that run.
The passing run 7 shows 3 to 5 ms. The forced GCs pause both threads, so the coarse value cannot separate the two threads.
In both, one profiled thread loses time: an 11 ms longer profiled arm, and a peer with no samples while the selected thread accepts six.
Profile results now record counted thread CPU and wall time. The counted CPU stops at disable, and the wall time includes the closing payment, so their difference is coarse. The next runs pass, so they do not test the descheduling hypothesis yet.
Barrier run `20261018T191852956-237f7bf8` shows the same signature with no forced GC: in `peer-bound` block 5, the runnable peer accepts no sample for about 5 ms.
That makes forced GC an unlikely sole cause. Each worker now records its kernel run-queue delay from `/proc/thread-self/schedstat`.
The three runs after that change pass. A 50% arm records at most 0.76 ms of run-queue delay. A 0% arm records 10.2 ms, which cannot break the bound because a 0% arm publishes no credit.
The host therefore does deschedule runnable profiled threads for several milliseconds.
Barrier run `20261018T193110908-a2f5b436` confirms the effect on prediction. Its two blocks outside the 10-point limit each have one long run-queue delay:
81.3 ms in a 0% arm (+11.73 points) and 17.1 ms in a 50% arm (-10.77 points). The other blocks have small delays.
Barrier run `20261018T203120679-31605bcf` fails `peer-bound` with a 5.28-point median and all differences positive.
The diagnostic run `artifacts/target-local-barrier-cpuset/20261018T203443644-3be38dc4` uses CPUs 0 and 2, one per physical core.
It shows no sign bias, but it also halves the CPU count, and the later unrestricted run shows none either. SMT stays a hypothesis.
It captures one debt failure: a woken peer starts 8.66 ms after its release while the selected thread publishes 4.5 ms.
The peer's whole-run run-queue delay is 8.64 ms. That is consistent with a wait for a CPU, but the diagnostic cannot place the delay in the interval.
Next diagnostic: record the run-queue delay at each explicit call, so a debt failure after a wake can be placed. The gates stay unchanged.

The review-fix runs on the current tree fail twice. The transition run `20261018T210459943-dfa8e472` passes, including criteria 9 to 11.
Prediction run `20261018T210529693-6820c454` fails `peer-longer` with a 5.69-point median. All five differences are negative, from -2.3 to -7.0 points.
The effective duration of each profiled 50% arm is 0.3 to 6.9 ms longer than its 0% arm. The median actual reduction is +2.1%.
The positive actual value is consistent with less SMT contention for the peer when the selected work is shorter.
A virtual speedup does not shorten the selected work, so it cannot reproduce that effect. This stays a hypothesis.
Barrier run `20261018T210647512-cd99aa09` fails `selected-dominant` block 1. The peer takes its first sample 10.2 ms after the selected thread's first sample.
By then the selected thread has published 5 ms of delay, so the peer's first sample records a 5 ms debt failure.
That peer's whole-run run-queue delay is 82.9 ms, and the selected thread's is 31.3 ms. Prediction run `20261018T204012284-1a60b3d6` and barrier run `20261018T204129945-675e5b4b`, the last runs before the review fixes, pass.
The fixes change no code in the timed window. The failures are kept, no rerun replaces them, and the bounds do not change.
A thread that the host leaves off CPU cannot pay, and the profiler reports that as a failure, as the no-forgiveness rule requires.

The prediction run `20261018T192337237-ab4134f4` passed with a `selected-longer` median difference of 4.09 points, and run `20261018T193237454-cc14a621` passed with 2.95 points.
Prediction run `20261018T210529693-6820c454` fails `peer-longer` as described above.

## Wait run-queue delay

[The wait-delay plan](target-local-wait-delay.md) keeps the debt of a runnable thread that waits for a CPU, so a debt failure stays a failure.
Each `TargetBarrier` wait now records its own run-queue delay, and the runners hash sources after CRLF to LF normalization.
Manifests before this change hash working-tree bytes, and some of those files had CRLF line endings, so a fresh clone cannot reverify those hashes.

The first runs on this design fail in all three suites. Each failure is kept, and no rerun replaces it.
- Barrier run `20261018T214145452-1972f479`: the wait records are complete in all 30 profile arms, with 0.20 ms of wait run-queue delay in total.
  `peer-bound` block 5 differs by +12.18 points. Its plain `original` arm runs 99.5 ms, its `optimized` arm 106.2 ms, and its `profile-zero` arm 108.7 ms.
  No profiled arm in that block has more than 0.34 ms of run-queue delay. The plain arms do not record run-queue delay, so scheduling delay in them stays possible. No profile arm records a debt failure.
- Prediction run `20261018T214023824-28b274dc`: `peer-longer` block 1 differs by +16.79 points. Its plain `optimized` arm runs 118.5 ms against 104.1 ms for `original`.
  The profiled arms stay near their siblings, and the overhead medians are 0.996 to 1.010.
- Transition run `20261018T213950841-bc5dbc42`: run 4 breaks the 2 ms debt bound under 19 forced GCs. The peer pays in 50 microsecond children without a gap,
  but it is 1.65 ms behind when the selected thread publishes 0.5 ms, so its debt reaches 2.10 ms. The forced-GC mode uses no barrier, so the wait change does not reach it.
In the barrier and prediction failures, plain or zero arms vary by 7 to 14% between arms, with at most 0.34 ms of run-queue delay measured in the profiled arms.
Five blocks for each configuration cannot separate that host variation from a profiler error.
Hypothesis, not shown: a quieter host or more blocks would give a stable pass. A persistent profiler error would survive both.

The review fixes changed the timed path, so all three suites ran again on the fixed tree.
- Transition run `20261018T221219867-6603996f` passes 10 of 10, with all lifecycle checks.
- Prediction run `20261018T221254076-9a1e3880` passes all three configurations, with median differences of 1.17 to 1.84 points and wait records complete.
- Barrier run `20261018T221415876-73afdb27` fails `alternating` block 3, which is invalid. The other 14 blocks are within the limits.
  The peer takes its first sample 5.8 ms after the selected thread's first sample, before its first wake. By then 3 ms is published, so the first sample records a 3 ms debt failure.
  The peer's whole-run run-queue delay in that arm is 6.66 ms. The wait diagnostic classifies the failure as before the first wake, so no wait delay applies.
This is the second captured debt failure at a thread's first sample. Both threads had several milliseconds of whole-run run-queue delay.

## Start run-queue delay

[The start-delay plan](target-local-start-delay.md) records the run-queue delay across the start barrier and the work start time of each worker.
The analyzer fails a run when a start record is missing or out of order. It places each debt failure before a first wake.

The runs on the reviewed tree:
- Transition run `20261018T233751688-5acdb8cc` passes 10 of 10.
- Barrier run `20261018T234001547-747aae56` passes all three patterns, with median differences of 1.45 to 3.78 points and start records complete.
- Prediction run `20261018T233840885-22a9d378` fails `selected-dominant` block 5, which is invalid. The other 14 blocks are within the limits.

In that block, the peer starts work 5.3 ms after the selected thread. Its start run-queue delay is 8.3 microseconds, and its residual run-queue delay is 0.
So run-queue delay does not cause this late start. The selected thread publishes 3 ms before the peer takes its first sample, 0.6 ms after its work start.
The peer journals no sample from its native start to 0.6 ms after its work start. Its task-clock count is within 1.1 ms of its native span, so the peer was on a CPU for most of that interval.
Fact: the perf event sets `exclude_kernel`, so a sample in kernel mode is not recorded. Hypothesis, not shown: the peer spent that interval in kernel mode. The records do not show where in the interval from its native start to its work start.

In the other 59 profile arms of both suites, the work start skew is at most 0.13 ms, except one of 1.19 ms. That arm has 1.30 ms of start run-queue delay.
Three captured debt failures now occur at a first sample. Only the latest run has start records, so the two earlier failures stay unplaced.

## Start trace

[The start-trace plan](target-local-start-trace.md) records three start points for each worker. Coz also keeps the debt of a new thread, so the late-start failure is legitimate under the CozNet debt bound.

The first runs read the on-CPU time from schedstat field 1, which lags for a running thread. Forced-GC run `20261019T001542478-b4d2cdb6` shows 3.94 ms on a CPU in a 0.77 ms segment.
That run fails run 8 by the 2 ms debt bound at 39 ms, after a 6.5 ms gap with no peer event. The peer has 5.3 ms of residual run-queue delay, which is consistent with a wait in that gap but does not place it.
Prediction run `20261019T001613652-a3f06d98` and barrier run `20261019T001733954-3bae192a` pass on that earlier tree.

The on-CPU time now comes from `CLOCK_THREAD_CPUTIME_ID`. The runs on the revised tree:
- Transition run `20261019T002340083-c152f4ca` passes 10 of 10.
- Prediction run `20261019T002411461-b833589f` passes all three configurations, with median differences of 0.93 to 2.62 points.
- Barrier run `20261019T002531212-f435da11` fails criterion 14. All 15 blocks are within the limits, but the 300 wait values are all zero.
  The same handle gives a whole-run run-queue delay above zero in 27 of 60 threads, so the reads are not stale. In the two previous barrier runs, only 8 and 6 of 300 waits had any delay.
  Criterion 14 can therefore fail with a correct reader on a quiet host. It stays fixed for this run, and the failure is kept.

No profile arm records a startup debt failure in these runs. The trace shows profiler work in the start path:
the `setup` segment, from the native start to the start barrier, has a median of 0.70 ms per worker and a largest value of 0.98 ms.
Its median on-CPU share is 64%, and 60 of the 120 setup segments have a major fault. It holds only `TargetCurrent.Set`, `TargetCurrent.Track`, and one trace point, after sampling starts.
The `barrier` segment is at most 0.19 ms, and the interval from the native start stamp to the first point is at most 20 microseconds.


## Start setup

[The start-setup plan](target-local-start-setup.md) sets the thread state before the native start. For barrier runs, criterion 27 replaces criterion 14 and counts all reads on the handle.

The runs on the reviewed tree:
- Transition run `20261019T004724440-fd9cd00c` passes 10 of 10.
- Barrier run `20261019T004925668-914e6b56` passes all criteria. The median setup segment is 0.0049 ms, the largest is 0.0064 ms, and the reader is fresh.
- Prediction run `20261019T004805873-c97ab2e5` keeps all 15 blocks within the limits, with median differences of 2.62 to 3.22 points. It fails criterion 26.
  Its median setup segment is 0.0049 ms, but one of the 60 segments takes 0.60 ms. The other 59 take at most 0.0066 ms.
  That segment has 0.14 ms on a CPU and 0.46 ms off the CPU. Its counters record no run-queue delay, no context switch, and no fault.
  The reads in one point are sequential, so a switch between two reads can escape the counters. The cause of this stall is not known.
  Hypothesis, not shown: the host paused the virtual CPU. In the same run, the largest `barrier` segment is 0.47 ms, and no criterion covers it.

The move removes the profiler setup from the sampled interval: the median setup segment falls from 0.70 ms to 0.0049 ms in both suites.
Criterion 26 also fails on one stall of unknown cause. No profile arm in these runs records a debt failure.
The retained records do not establish the cause of such stalls. Guest-side causes stay open.
A manual check in the same container image on 2026-10-19, outside the qualification runs, read zero steal time on every CPU in `/proc/stat` before and after a 2 s sleep.
So this guest gives no record that can confirm or exclude a host pause of a virtual CPU. A host with steal-time accounting, or bare metal, can test that hypothesis.
Criterion 26 does not change.

## GC windows

[The GC-window plan](target-local-gc-windows.md) records a `Stopwatch` window around each forced collection and reports the window time in each debt-failure interval and each worker's largest event gap.

All three suites pass on the reviewed tree:
- Transition run `20261019T011520304-41a7c250` passes 10 of 10, with complete GC window records. No run records a debt failure.
  Each run has 19 or 20 windows, with 6.75 to 10.28 ms of window time and 5.55 to 8.87 ms of pause time from `GC.GetTotalPauseDuration`.
  The largest event gap of each run is 1.59 to 2.49 ms, and 0.12 to 1.39 ms of window time falls inside it. A window is an upper bound on its collection time.
- Prediction run `20261019T011552600-3ac91378` passes all three configurations, with median differences of 0.80 to 3.22 points. The largest setup segment is 0.023 ms.
- Barrier run `20261019T011713329-52b0bbca` passes all three patterns, with median differences of 1.10 to 4.22 points. The largest barrier segment is 0.52 ms, which no criterion covers.

The four retained forced-GC debt failures predate the windows, so their collections stay unplaced.

## ReJIT check

[The ReJIT plan](target-local-rejit.md) adds a test CLR profiler that requests a ReJIT of `Work.selected` during a bound run.
Prediction run `20261019T014112845-eec890b5` and barrier run `20261019T014236242-6440bd58` pass all criteria.
Transition run `20261019T014043978-f7a0ddbc` passes the ReJIT check and every other lifecycle check.
In the ReJIT check, `RequestReJIT` returns 0 and one ReJIT compilation finishes with status 0. The perf map then holds two `[Optimized]` entries for `Work.selected`, and the run fails with `BindingError`.
The transition run fails by forced-GC run 7, which breaks the 2 ms debt bound. It is the fifth current-design forced-GC debt failure and the first with GC windows.
- The peer has no event from 24.45 ms to 29.68 ms, and its failure interval is 5.75 ms. Only one window, of 0.22 ms, overlaps that interval.
- The selected thread journals a sample about every 1 ms from 25.85 ms to 28.86 ms. So no collection suspends both workers for long in the gap. A short suspension inside the 0.22 ms window stays possible.
- The peer has 9.14 ms of whole-run run-queue delay, with no waits and no start delay. That is consistent with a run-queue wait in the gap, but the records do not place it.
So forced collections cover at most 0.22 ms of this failure interval. The records do not establish its cause, and the failure stays.
In the same run, the selected thread has an 8.88 ms event gap from 8.44 ms to 17.31 ms with no window and 8.11 ms of whole-run run-queue delay. It records no debt failure there.
The lifecycle plan said that no check produces a ReJIT until the next run, because the transition runner hashes it.

## Linux backend file

The native ABI binding and the Linux readers moved unchanged from `src/CozNet/TargetLocalProfiler.fs` to `src/CozNet/TargetLocalLinux.fs`.
`TargetLocalProfiler.fs` keeps the perf-map binding, `TargetBarrier`, and the run loop. The lifecycle plan now names the ReJIT check.
All three suites pass on that tree:
- Transition run `20261019T020010673-c0788652` passes 10 of 10, with complete GC windows and every lifecycle check, including ReJIT.
- Prediction run `20261019T020044104-92e8b8b1` passes all three configurations, with median differences of 0.93 to 1.95 points.
- Barrier run `20261019T020204625-4ef1deb4` passes all three patterns, with median differences of 0.94 to 1.32 points.
  Its largest `barrier` start segment is 5.04 ms, against 0.47 and 0.52 ms in earlier runs. No criterion covers that segment, and no profile arm records a debt failure.

## Causal profile

[The causal profile plan](target-local-causal-profile.md) adds `TargetCausalDesign.tryCreate` and `TargetCausalProfile.run`. The driver runs a seeded random design of experiments in one process.
Profile run `20261019T024109669-c30228ca` passes all six points on its first run. Each point has 5 valid blocks.
- `Work.selected` predicts 24.43%, 49.17%, and 48.73% at speedups 25, 50, and 75. The real optimizations give 24.74%, 50.43%, and 50.24%.
- `Work.peer` predicts 0.38%, -1.20%, and -1.70%. The real optimizations give 1.06%, -0.11%, and 1.15%.
- The median absolute differences are 0.96 to 3.60 points, and the largest block difference is 6.28 points.
So the profile shows the plateau of `Work.selected` and the zero line of `Work.peer`, and real optimizations confirm both.

On the same tree, transition run `20261019T024210440-65ed47ee` passes 10 of 10, and prediction run `20261019T024239257-fae8a160` passes.
Barrier run `20261019T024401530-b69ae382` keeps all 15 blocks within the limits, but it fails criterion 26.
One of its 60 setup segments takes 0.70 ms, with 9.3 microseconds on a CPU. Its counters record no run-queue delay, no switch, and no fault. The other 59 take at most 0.0074 ms.
This is the second such stall. The retained records do not establish its cause. Both stalls are in block 1, in the `profile-speedup` arm, on worker 0. Two cases do not establish a pattern.

## Throughput progress points

[The throughput plan](target-local-throughput.md) adds `Throughput` progress points: each experiment runs for a fixed time and counts visits.
Throughput run `20261019T032027341-60c2ab7c` passes all six points on its first run. Each point has 5 valid blocks.
- `Work.selected` predicts 9.10%, 23.83%, and 48.50% at speedups 25, 50, and 75. The real optimizations give 10.82%, 25.28%, and 49.68%.
- `Work.peer` predicts 17.22%, 38.41%, and 65.44%. The real optimizations give 18.68%, 39.97%, and 66.58%.
- The median absolute differences are 0.76 to 3.09 points, and the largest block difference is 5.26 points. No experiment breaks the debt bound.

The other suites on the same tree:
- Prediction run `20261019T032259730-367c4746` and barrier run `20261019T032421198-d2441fed` pass all criteria.
- Latency profile run `20261019T032133781-6cc15ea3` fails two points, each by one block in block 1. Its other four points pass.
  In that block, the plain `selected-75` arm gives 36.0%, against a prediction of 47.4% and a model value of 50%. The block difference is 11.41 points.
  The plain `peer-75` arm gives 4.5%, against a prediction of -6.0% and a model value of 0%. The block difference is 10.42 points.
  The records do not show which arm caused each difference. The first latency profile run passed all six points.
- Transition run `20261019T032230942-d027dbb5` fails by forced-GC run 10 at the 2 ms debt bound, and every lifecycle check passes.
  One GC window covers 1.16 ms of the 5.77 ms failure interval. The failing worker has 3.69 ms of whole-run run-queue delay. The records do not establish the cause.

## Blocking queue

[The queue plan](target-local-queue.md) adds `TargetQueue`, a bounded queue whose waits follow the wake rule, and a pipeline qualification with one producer and one consumer.
Pipeline run `20261019T040306290-64463c6b` passes five of its six points and fails `peer-50` by its median.
- `Work.selected` predicts 23.84%, 49.72%, and 48.62% at speedups 25, 50, and 75. The real optimizations give 26.50%, 49.95%, and 49.71%.
  So the profile shows the plateau of the producer through real blocking waits, and real optimizations confirm it.
- `Work.peer` predicts -0.70%, -3.91%, and -2.53%. The real optimizations give 0.97%, 2.85%, and 1.22%.
  `peer-50` has a median absolute difference of 6.76 points, above the 5-point limit. Across the three `peer` points, 13 of 15 block predictions are below their actual gains.
  In block 1 at `peer` 50%, the effective time falls from 120.6 ms to 99.0 ms, but the visits fall from 168 to 130.
  Hypothesis, not shown: the producer, which sets the rate, pays the delay of the consumer with overhead that the delay does not count.

The other suites on the same tree:
- Throughput run `20261019T040411141-1b17380b`, prediction run `20261019T040632196-702f8052`, and barrier run `20261019T040754718-d668c4c1` pass all criteria.
- Latency profile run `20261019T040508680-38464c4a` fails, because block 3 is invalid. Its `selected` experiment at 75% breaks the 2 ms debt bound.
  The other four blocks give median differences of 0.54 to 2.76 points.
- Transition run `20261019T040604415-1a034540` fails by forced-GC run 9 at the 2 ms debt bound, and every lifecycle check passes. Two GC windows cover 0.68 ms and 0.23 ms, together 0.91 ms, of the 6.50 ms failure interval.

## Experiment diagnostics

[The diagnostics plan](target-local-experiment-diagnostics.md) adds a record for each worker to each causal experiment, and a coarse work ratio for each point.
Pipeline run `20261019T043536804-514995d6` passes five of six points again. This time `peer-50` passes, and `peer-75` fails by one block of 14.82 points.
- At `peer` 50%, four of five blocks predict -3.9% to -6.6%. In those blocks, the work ratio of the producer is 1.042 to 1.067. In block 5, the ratio is 0.994 and the prediction is +0.9%.
  So the prediction tracks the producer time outside measured child payments, for each visit. The producer sets the rate in this pipeline.
  Both values use the same durations and visits, so this association is descriptive and does not show a cause.
  The median signed differences are +1.60 points at `peer` 25%, -2.99 points at `peer` 50%, and +3.14 points at `peer` 75%, so only `peer` 50% keeps a negative bias in this run.
  Hypothesis, not shown: overhead of each payment that the measured child time does not hold.
- The `selected` points predict 24.71%, 49.61%, and 49.58%. The real optimizations give 24.80%, 49.75%, and 47.70%.

The other suites on the same tree:
- Throughput run `20261019T043639774-d3af17e6`, transition run `20261019T043835718-5c15e486`, and prediction run `20261019T043903739-5859c385` pass all criteria.
- Latency profile run `20261019T043738634-485c3ea9` fails `peer-50` and `peer-75`. `peer-50` has a median of 5.52 points and one block of 10.91 points. `peer-75` has one block of 12.72 points. Both work ratios are near 1.
- Barrier run `20261019T044024104-2513fe0c` fails `barrier-selected-dominant` block 2. The peer breaks the 2 ms debt bound 2 microseconds after a dependency wake.
  Its wait has 7.40 ms of run-queue delay. The other 14 blocks are within the limits.

See [the roadmap](roadmap.md) and [the summary](../summary.md).
