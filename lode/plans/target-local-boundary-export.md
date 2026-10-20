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

## Capture result

Capture `20261019T235414964-e2bcd66a` ran once, from 2026-10-19T23:54:14Z to 23:55:50Z, and exited with code 0. This section is the report of criteria 159 to 163.

### Identity

- Command: `pwsh -NoProfile -File native/VerifyTargetLocalBottleneck.ps1`, without `-Probe`. The manifest records `probe: false`, `diagnostic: true`, seed 20261018, 5 blocks, peers 8 and 2, and `inputsUnchanged: true`.
- The tree was commit `63de22c`. `git diff --stat 156db99 HEAD -- src samples native tests build` is empty, so the implementation files are those of `156db99`.
- All 29 source hashes of the manifest agree with the committed files. The build tree also held the unrelated uncommitted change to `src/Directory.Build.targets`, as in all earlier runs.
- Binary hashes: `CozNet.TargetLocal.dll` `BF5B991BF888B001BE6F06EACCACFB77911EC7897BBB6A49C78FA0366D47207A`, `libCozNet.TargetLocal.so` `47787E99D8F01062DBE01CDE7B650E808906C766717DCC0A330AE388D45E193F`, and `CozNet.dll` `C5A5642432C95061AFFDB734FE6FCCDE6ACCCBEFAD8AFE35F6B6AE2256990CAD`.

### Availability

- All 50 arms exited with code 0. Each peer has 5 protocol-valid blocks, so the common valid blocks are 1 to 5. The evidence is valid in all 10 profiles.
- All 50 boundary observations are present: 40 experiments and 10 warm-ups. The analyzer excludes none.
- Each exclusion reason has a count of 0: a failed experiment, a missing boundary, a wrong shape, reads out of order, derived values that differ from the reads, no arrivals, an effective duration that is not positive, and a negative occupancy.
- No baseline is zero or undefined, and no signed total is 0 or less. So no gain is undefined.
- The eligible blocks are 1 to 5 for the clipped, signed, and boundary-derived predictions, for both peers and at all three speedups. The peer comparison uses the same blocks.

### Criterion 137

- The unchanged rule gives "congestion supported": S8 is -13.42 against the limit -8.0, S2 is -4.73 against the band of 5.0 points, R2 is 0.053 against the limit 0.10, and 5 blocks are common.
- S2 is 0.27 points inside the band.
- The earlier labels are "congestion supported" for `20261019T213806586-069c2adf`, and "not decisive: too few valid blocks" for `20261019T222832093-272334f5` and the probe capture `20261019T225711474-4ea91d27`.
- The captures are not pooled, and this capture is not a qualification claim. The label supports the congestion hypothesis and does not prove it.

### Predictions against the same plain arm

Each row compares the predictions of one profile experiment with the actual gain of the plain arms of the same block and peer. Boundary is the pinned upstream formula from the boundary reads, against the same block at 0%. The occupancy is the stop difference of the boundary reads.

`selected-25`, gains in percent:

| Peer | Block | Actual | Clipped | Signed | Boundary | Occupancy at 0% and at the speedup |
| ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 8 | 1 | +21.74 | +18.97 | +18.97 | +24.60 | 1, 1 |
| 8 | 2 | +20.91 | +16.65 | +16.65 | -56.59 | 1, 2 |
| 8 | 3 | +20.86 | +18.54 | +18.54 | +23.03 | 2, 2 |
| 8 | 4 | +20.79 | +18.14 | +18.14 | +22.88 | 1, 1 |
| 8 | 5 | +20.57 | +19.78 | +19.78 | +23.96 | 1, 1 |
| 2 | 1 | +23.22 | +23.67 | +23.67 | +25.43 | 1, 1 |
| 2 | 2 | +25.50 | +21.91 | +21.91 | -49.15 | 1, 2 |
| 2 | 3 | +22.16 | +22.12 | +22.12 | -47.39 | 1, 2 |
| 2 | 4 | +22.10 | +24.26 | +24.26 | +25.51 | 1, 1 |
| 2 | 5 | +20.55 | +22.85 | +22.85 | +24.95 | 1, 1 |

`selected-50`, gains in percent:

| Peer | Block | Actual | Clipped | Signed | Boundary | Occupancy at 0% and at the speedup |
| ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 8 | 1 | +43.86 | +30.09 | +30.09 | +46.94 | 1, 1 |
| 8 | 2 | +43.17 | +40.13 | +40.13 | -1.08 | 1, 2 |
| 8 | 3 | +39.20 | +39.37 | +39.37 | +73.99 | 2, 1 |
| 8 | 4 | +37.90 | +39.99 | +39.99 | -0.59 | 1, 2 |
| 8 | 5 | +42.98 | -1.80 | -1.80 | -21.28 | 1, 2 |
| 2 | 1 | +47.19 | +46.34 | +46.34 | +50.85 | 1, 1 |
| 2 | 2 | +47.38 | +46.73 | +46.73 | +2.03 | 1, 2 |
| 2 | 3 | +48.07 | +44.66 | +44.66 | +49.26 | 1, 1 |
| 2 | 4 | +47.04 | +41.55 | +41.55 | +48.00 | 1, 1 |
| 2 | 5 | +37.79 | +44.56 | +44.56 | -1.73 | 1, 2 |

`selected-75`, gains in percent:

| Peer | Block | Actual | Clipped | Signed | Boundary | Occupancy at 0% and at the speedup |
| ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 8 | 1 | +62.37 | +45.73 | +45.80 | +47.67 | 1, 2 |
| 8 | 2 | +62.95 | +51.02 | +51.02 | +47.15 | 1, 2 |
| 8 | 3 | +63.92 | +49.34 | +49.34 | +86.39 | 2, 1 |
| 8 | 4 | +60.71 | +47.29 | +47.29 | +45.22 | 1, 2 |
| 8 | 5 | +61.97 | +48.89 | +48.89 | +46.42 | 1, 2 |
| 2 | 1 | +70.70 | +64.87 | +65.56 | +72.40 | 1, 1 |
| 2 | 2 | +70.61 | +65.91 | +66.04 | +74.93 | 1, 1 |
| 2 | 3 | +65.11 | +64.97 | +65.28 | +73.09 | 1, 1 |
| 2 | 4 | +71.06 | +66.32 | +66.68 | +74.58 | 1, 1 |
| 2 | 5 | +70.84 | +65.21 | +65.71 | +73.31 | 1, 1 |

Aggregates over blocks 1 to 5. Each error is the median of the block values of prediction minus actual, in points. The viewer gain is the pinned viewer aggregate of the boundary values. The last column is the median of the actual block gains.

| Peer | Speedup | Clipped error | Signed error | Boundary error | Viewer boundary gain | Median actual gain |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 8 | 25% | -2.65 | -2.65 | +2.18 | +10.48 | +20.86 |
| 2 | 25% | +0.45 | +0.45 | +2.21 | -4.12 | +22.16 |
| 8 | 50% | -3.03 | -3.03 | -38.49 | +28.99 | +42.98 |
| 2 | 50% | -0.86 | -0.86 | +0.96 | +29.59 | +47.19 |
| 8 | 75% | -13.42 | -13.42 | -15.50 | +59.90 | +62.37 |
| 2 | 75% | -4.73 | -4.58 | +3.52 | +73.67 | +70.70 |

- The signed value equals the clipped value, except in the experiments with negative transactions: peer 8 block 1 and all peer 2 blocks at 75%. There, the two differ by at most 0.69 points.
- The stop occupancy is 1 in 32 observations and 2 in 18. It is never 0.
- The boundary values group by the pair of occupancies. With 1 and 1, they are 0.96 to 7.98 points above the actual gain. With 2 and 2, the one value is +23.03. With 1 and 2, they are -56.59 to -47.39 at 25%, -21.28 to +2.03 at 50%, and +45.22 to +47.67 at 75%. With 2 and 1, they are +73.99 and +86.39.
- In this workload, one stop sample of 1 or 2 changes the pinned value of one experiment by a factor of 2. The actual gain of peer 8 at 75% falls between the groups.
- The paired occupancy surrogate of the analyzer is a different value. It is not the upstream expectation.

### Boundary spans and race counts

Times in nanoseconds. No estimator uses the warm-up observations.

| Span | Observations | Minimum | First quartile | Median | Third quartile | Maximum |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Stop read, experiments | 40 | 339 | 539 | 630 | 735 | 1272 |
| Stop read, warm-ups | 10 | 507 | 582 | 603 | 709 | 729 |
| First stop clock to the clock after `Stop`, experiments | 40 | 658 | 1026 | 1153 | 1329 | 2327 |
| First stop clock to the clock after `Stop`, warm-ups | 10 | 150230 | 183917 | 191290 | 201753 | 207755 |
| Clock after `Stop` to the drain end, experiments | 40 | 63116 | 408914 | 598928 | 784564 | 946566 |
| Clock after `Stop` to the drain end, warm-ups | 10 | 123459 | 430462 | 583221 | 736479 | 801488 |

- The largest experiment spans are in peer 2 block 4 at 50%: 1272 for the stop read and 2327 to the clock after `Stop`. Peer 8 block 4 has stop reads of 797 to 991 and spans to the clock after `Stop` of 1324 to 1589.
- In all 10 warm-ups, 149678 to 207153 of the span comes after the stop read and before the clock after `Stop`. This capture does not measure the cause.
- The drain is shortest in peer 2 block 1 at 50%, and longest in peer 8 block 2 at 75%.
- `countMinusStampedBegins`, `countMinusStampedEnds`, and `beginsStampedAfterStopCalled` are 0 in all 50 observations. G does not change between the two stop clocks in any observation.
- These brackets bound some effects of the observation. They cannot measure all the added delay of the monitor or prove atomicity. A count of 0 for `beginsStampedAfterStopCalled` is a lower bound.
- These are descriptions. They set no pass limit.

### Bounded conclusion

- The export works in a live paired capture. Every boundary is present and consistent, and no value is undefined.
- Each experiment gives one stop occupancy, and each peer and speedup gives 5. These few samples cannot establish a stationary latency estimate or a causal mechanism.
- The boundary values do not change the label of criterion 137, the block validity, or the public estimator.
- All timing collection stopped after this capture.

See [target-local latency estimator](target-local-latency-estimator.md), [target-local latency model](target-local-latency-model.md), and [target-local status](target-local-status.md).
