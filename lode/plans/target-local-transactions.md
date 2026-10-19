# Target-local latency progress points

## Problem

Coz has three kinds of progress point: end to end, throughput, and latency. A latency point is a pair of `COZ_BEGIN` and `COZ_END` visits around one transaction.
CozNet has `EndToEnd` and `Throughput` only. So it cannot predict the effect of an optimization on the mean latency of a transaction.

## Coz rule

Pinned Coz counts arrivals at `COZ_BEGIN` and departures at `COZ_END`. At the end of each experiment, it logs the arrivals, the departures, and the difference of the two counters, which is the number of transactions in flight at that moment.
The viewer then applies Little's law: the mean latency is the difference divided by the arrival rate, `difference / (arrivals / duration)`.
See `libcoz/progress_point.h` and `viewer/ts/profile.ts` in the pinned Coz source under `lode/tmp/upstream-coz`.

## Decision

`TargetProgressPoint.Latency durationMs` runs each experiment for a fixed time, as `Throughput` does.
`TargetProgressCounter.Begin()` returns a ticket, and `TargetProgressCounter.End ticket` records one departure and the wall time from the begin to the end.
Each `TargetCausalExperiment` gains `Arrivals` and `LatencyNs`, the sum of the wall latency of the completed transactions. `Visits` is the count of departures.

CozNet differs from Coz in one point. Coz uses the difference at one instant, which is a small integer. One CozNet experiment covers one point in one block, so that sample is too coarse.
CozNet uses the mean wall latency W of the completed transactions. A virtual speedup stretches the wall time of all threads by the elapsed time T over the effective time E, so the virtual latency is `W * E / T`.
This equals Little's law with the time average of the transactions in flight, if the edge transactions are left out.
The scaling by `E / T` assumes that the global delay stretches each transaction as it stretches the whole run. Work outside the transactions can bias it, and the workload has none.
The prediction is `1 - (W * E / T) / (W0 * E0 / T0)`, where W0, E0, and T0 are those of the block baseline. An experiment without latency gives no prediction. A real optimization is measured as `1 - W / W0` from the plain arms.

## Qualification

The workload `transaction` runs two workers for 120 ms in each experiment. They form a pipeline through a `TargetQueue` of 4 items:
- Worker 0 calls `Begin`, runs `Work.selected 40`, and enqueues the ticket.
- Worker 1 dequeues a ticket, runs `Work.peer 8`, and calls `End`, which counts the visit.
The producer sets the rate at every point, because its fastest arm, 10 rounds, is still slower than the consumer at 8 rounds. So the queue is usually empty, and the latency of a transaction is about the sum of both parts.
If the consumer set the rate, the queue would fill, and each transaction would wait behind up to 4 items. The workload avoids that case.
So the model gives `Work.selected` 20.8%, 41.7%, and 62.5% at speedups 25, 50, and 75, and `Work.peer` 4.2%, 8.3%, and 12.5%. The model leaves out the queue handoff and any queue wait.
A throughput point would give `Work.peer` 0% in this pipeline. At `peer-50` and `peer-75`, the model differs from 0% by more than the 5-point limit, so those two points test the latency rule. At `peer-25` it does not.
At `selected-75`, the margin is small: 10 producer rounds against 8 consumer rounds plus one queue handoff. If the consumer sets the rate there, the queue fills, and the real gain falls.
An optimized arm reduces the selected rounds to 30, 20, and 10, or the peer rounds to 6, 4, and 2. The criteria use the measured gains, not the model values.
The sample modes are `transaction-profile` and `plain-transaction`. `VerifyTargetLocalProfile.ps1 -Workload transaction` and the build target `TargetLocalTransactionVerify` run the suite.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 81 and change none.

82. Unit tests show that the latency prediction is `1 - (W * E / T) / (W0 * E0 / T0)`, that it is `None` without departures or with a failed experiment or baseline, and that `Begin` and `End` count arrivals, departures, and latency.
    `tryCreate` must reject a `Latency` duration outside 1 to 60000 ms with `InvalidDuration`.
83. Each transaction profile record holds mode `transaction-profile`, the fixed design, the warm-up, the seed and rotation, and `Arrivals` and `LatencyNs` for each experiment. Each passed experiment has at least 100 departures.
    Each plain transaction record has mode `plain-transaction`, the rounds of its arm, a 120 ms duration, at least 100 departures, and its arrivals, departures, and summed latency.
    In each passed experiment and each plain record, the arrivals equal the departures, because the consumer drains the queue before it ends.
84. Each of the six transaction points has 5 valid blocks, a median absolute difference of at most 5 percentage points, and each block within 10. The positions are balanced as in criterion 57.
85. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local throughput](target-local-throughput.md), [target-local queue](target-local-queue.md), and [target-local status](target-local-status.md).
