# Target-local boundary export

## Supervisor decision

The supervisor decided on 2026-10-19 to move toward a diagnostic export of the experiment boundaries, and not to close the estimator line:
- The missing observations matter for port fidelity, although they cannot prove a causal mechanism.
- No new live capture and no change to the public estimator in this step. The export is opt-in diagnostic evidence, so the public result formats do not change.
- Resolve the snapshot contract first. Do not add locks to `Begin` or `End` to make the diagnostic look exact.
- If a common-instant snapshot needs new synchronization or timed-path instrumentation, send the tradeoff before any implementation.

## Upstream read order

Coz `256e3c389424d6a1691ee5d18b5ecb5a6c1001ec`, cached under `lode/tmp/upstream-coz`:
- A latency point keeps two relaxed atomic counters. `COZ_BEGIN` and `COZ_END` increment them with `__atomic_add_fetch(..., __ATOMIC_RELAXED)` (`include_coz.h:141-154`).
- Start of an experiment: the profiler saves each latency point, which reads the begin count and then the end count (`libcoz_profiler.cpp:303-309`, `libcoz_progress_point.h:150-153`). Then it reads the clock (`libcoz_profiler.cpp:320`) and then the global delay (`:322`).
- End of an experiment: it reads the global delay (`:361`) and then the clock (`:362`). It reads the counters later, while it writes the log (`:402-441`). Each value reads them again: `get_begin_delta`, `get_end_delta`, and `get_difference` (`libcoz_progress_point.h:164-174`).
- The experiment stays active until after the log (`libcoz_profiler.cpp:477`). So threads can change the counters during these reads.
- The cached files do not contain `get_time`. So this note does not state its clock.

So upstream takes no common-instant snapshot. Its counters, its clock, and its global delay are separate reads, and the end counters come after the end clock.

## CozNet behavior at the boundaries

- `TargetProgressCounter.Begin` settles, increments the arrivals, and then reads the clock. `End` settles, reads the clock, records the transaction, and then increments the visits. `Stop` only writes the stop flag.
- The producer checks `Stopped` before each `Begin`. So it can pass the check before `Stop` and begin after it.
- `stopAfter` waits until a worker works, sleeps for the duration, and then calls `Stop`. The consumer then drains the queue.

## Contract

The export makes upstream-like ordered reads and evaluates the pinned formula with G rebuilt from the journal. It is not a linearizable snapshot.
It does not reproduce the upstream reads of the global delay, or the repeated counter reads of the upstream log. Its counters belong to one experiment, and the upstream counters belong to the whole process.
- Start observation, when a worker first works: read the arrivals, then the visits, then the clock.
- Stop observation, after the requested duration: read the clock, then the arrivals, then the visits, then the clock again, then call `Stop`, then read the clock again.
- G at an observation time is the journal publishes ordered by time, as in `virtualLatency`. It is not a read of the global delay at that instant.
- arrivals = stop arrivals - start arrivals, departures = stop visits - start visits, and difference = stop arrivals - stop visits. The counter is new for each experiment, so its absolute counts start at 0 at the experiment, not at the process.
- effective duration = (first stop clock - start clock) - (G at the first stop clock - G at the start clock).
- The upstream latency is difference / (arrivals / effective duration). It is undefined, with a stated reason, when there are no arrivals, the effective duration is not positive, or the difference is negative.
- The export also gives signed differences between the counts and the transaction stamps at the first stop clock, so the races show:
  - `countMinusStampedBegins` is the stop arrivals minus the begins stamped at or before the first stop clock. A positive value is a begin that counted before its stamp, because `Begin` counts before it stamps, or that came during the reads.
  - `countMinusStampedEnds` is the stop visits minus the ends stamped at or before the first stop clock. A negative value is an end stamped before its count, because `End` stamps before it counts. A positive value is an end that came during the reads.
  - `beginsStampedAfterStopCalled` counts the begins stamped after the clock read that follows `Stop`, from a producer that passed its check before `Stop`. It is a lower bound, because a begin between `Stop` and that clock read is not in it.
- The race counts use only completed transactions. This is correct while the consumer drains the queue.
- It keeps the requested duration, the observed stop clocks, the clock after `Stop`, and the drain end, which is the last end of a transaction. The drain end and the drain totals are not used in the formula.

Only `stopAfterObserved` makes these reads, on the monitor thread. Only the diagnostic mode `transaction-selected-profile` uses it. It adds no synchronization and no instrumentation to `Begin` or `End`.
In that mode, `Stop` comes after three more reads on the monitor thread. Their delay is not measured, and a preemption of the monitor can lengthen it. The other modes call the unchanged `stopAfter`.

## Analysis

- `native/AnalyzeTargetBottleneck.py` adds `upstreamBoundary` to the summary. For each peer and speedup, it gives the upstream gain of each block against the same block at speedup 0, and the viewer aggregate over the blocks.
- The viewer aggregate is the difference averaged by duration, divided by the summed arrivals over the summed durations. Its baseline and treatment use the same blocks: those with a valid boundary at both speedups.
- An observation is excluded, with its reason, when its experiment failed, its boundary is missing or malformed, its derived counts differ from its reads, its reads are out of order, or its boundary has a reason.
- These values do not change the label of criterion 137, the block validity, or any earlier criterion.

## Stale claims corrected

- The latency estimator plan called the occupancy average the expected upstream value. It is a surrogate, and the plan now says so.
- The scratch revisions 1 and 2 of the estimator contract review are superseded by [the latency model plan](target-local-latency-model.md).

## Acceptance criteria

These criteria are fixed before the reviews. They add to criteria 1 to 154 and change none.

155. Unit tests of the boundary evidence cover:
     - a publish at an observation time and a publish between the stop reads;
     - a begin counted before its stamp, an end stamped before its count, and a begin after `Stop`;
     - zero arrivals, a non-positive effective duration, a negative occupancy, and no transactions.
156. A unit test of `stopAfterObserved` covers the read order, the counts at both observations, the requested duration, and a run that never works.
157. Unit checks of the analyzer cover a missing or malformed boundary, reads out of order, a boundary with a reason, undefined upstream values, and the viewer aggregate.
158. No live capture runs in this step.

## Capture phase

The supervisor approved on 2026-10-19 exactly one live paired capture, to exercise the boundary evidence. Criterion 158 stays true for the implementation phase, which ran no capture.
- Implementation: the reviewed commit `156db99`. The capture runs on a later commit that changes only plans and docs, so its implementation files equal those of `156db99`.
- Command: `pwsh -NoProfile -File native/VerifyTargetLocalBottleneck.ps1`, without `-Probe` and without other instrumentation. The work, the seed, the order, the duration, the peer settings, the sample period, the capacity bounds, and the warm-up do not change.
- No retry, no replacement of a failed arm, and no pooling with other captures. All earlier captures and all criteria stay. The capture is not a qualification claim.
- If the harness cannot run as planned, the capture stops, and no unreviewed timing change is made. All timing collection stops after this one capture, whatever its outcome.

These criteria are fixed before the capture. They add to criteria 1 to 158 and change none.

159. The capture runs exactly once, and it is retained with its manifest, whatever its outcome.
160. The report gives the boundary availability and each exclusion reason, the protocol-valid blocks, and the unchanged label of criterion 137.
161. The report compares the clipped, signed, and boundary-derived predictions with the same plain arm, on the same eligible blocks for each comparison. It gives the values of each block before any aggregate.
     A comparison between peers uses only the blocks that are eligible for both. A gain from a zero or undefined baseline is reported as undefined.
162. The report gives the distributions and outliers of the stop-read span, the span from the first stop clock to the clock after `Stop`, the signed count-versus-stamp differences, the begins after `Stop`, and the drain extent. It sets no new pass limit.
     These brackets bound some effects of the observation, but they cannot measure all the added delay of the monitor or prove atomicity.
163. A few boundary samples cannot establish a stationary latency estimate or a causal mechanism. The report states this.

See [target-local latency estimator](target-local-latency-estimator.md), [target-local latency model](target-local-latency-model.md), and [target-local status](target-local-status.md).
