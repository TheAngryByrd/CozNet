# Target-local barrier qualification

Evidence: `artifacts/target-local-barrier/20261018T193110908-a2f5b436`.

`TargetLocalBarrierVerify` ran once with seed 20261018 on kernel `5.15.153.1-microsoft-standard-WSL2`, x64, with 4 CPUs.
The criteria are in [the prediction plan](../../../lode/plans/target-local-prediction.md#barrier-qualification), fixed before the first barrier run.
The runner hashes that file, [the blocking plan](../../../lode/plans/target-local-blocking.md), and the sources. All hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

Two registered threads run 10 phases. In each phase the selected thread runs A rounds and the peer runs B rounds. Both then meet at a `TargetBarrier`.
Each block runs `profile-phases 0`, `profile-phases 50`, `plain-phases <pattern> 100`, and `plain-phases <pattern> 50` in a random order.

| Configuration | Pattern | Expected actual | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Median skipped delay | Result |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `barrier-alternating` | `alternating` | about 40% | 5 / 5 | 37.05% | 38.80% | 3.24 pp | 11.73 pp | 51.5 ms | Fail |
| `barrier-selected-dominant` | `selected-dominant` | about 50% | 5 / 5 | 47.88% | 49.65% | 1.37 pp | 3.13 pp | 102.5 ms | Pass |
| `barrier-peer-bound` | `peer-bound` | about 0% | 5 / 5 | 1.66% | 1.62% | 2.12 pp | 5.49 pp | 0.0 ms | Pass |

| Configuration | Block | Predicted | Actual | Difference | Global delay (ms) | Skipped delay (ms) | Handler share at 0% | Run-queue delay at 0% (ms) | Run-queue delay at 50% (ms) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `barrier-alternating` | 1 | 36.50% | 39.74% | -3.24 pp | 119.5 | 53.0 | 0.080% | 0.01 | 0.14 |
| `barrier-alternating` | 2 | 39.67% | 37.45% | +2.22 pp | 116.5 | 51.5 | 0.090% | 0.01 | 0.01 |
| `barrier-alternating` | 3 | 37.05% | 38.80% | -1.74 pp | 116.0 | 50.5 | 0.073% | 0.47 | 0.01 |
| `barrier-alternating` | 4 | 50.44% | 38.72% | +11.73 pp | 116.5 | 51.5 | 0.099% | 81.26 | 0.02 |
| `barrier-alternating` | 5 | 29.48% | 40.25% | -10.77 pp | 121.0 | 63.5 | 0.097% | 0.00 | 17.14 |
| `barrier-selected-dominant` | 1 | 48.76% | 48.28% | +0.48 pp | 203.5 | 102.5 | 0.070% | 0.24 | 0.01 |
| `barrier-selected-dominant` | 2 | 47.88% | 49.06% | -1.19 pp | 210.0 | 104.5 | 0.088% | 0.13 | 0.00 |
| `barrier-selected-dominant` | 3 | 49.06% | 50.43% | -1.37 pp | 208.0 | 100.5 | 0.084% | 0.08 | 0.01 |
| `barrier-selected-dominant` | 4 | 46.52% | 49.65% | -3.13 pp | 212.0 | 104.5 | 0.065% | 0.00 | 0.08 |
| `barrier-selected-dominant` | 5 | 47.77% | 50.34% | -2.56 pp | 212.5 | 102.0 | 0.092% | 0.01 | 0.34 |
| `barrier-peer-bound` | 1 | 2.37% | 2.25% | +0.13 pp | 25.5 | 0.0 | 0.096% | 0.01 | 0.00 |
| `barrier-peer-bound` | 2 | 3.19% | 4.16% | -0.97 pp | 25.5 | 0.0 | 0.114% | 0.01 | 0.01 |
| `barrier-peer-bound` | 3 | -3.87% | 1.62% | -5.49 pp | 26.6 | 0.1 | 0.145% | 0.00 | 0.02 |
| `barrier-peer-bound` | 4 | 1.66% | -0.47% | +2.12 pp | 26.0 | 0.0 | 0.112% | 0.07 | 0.00 |
| `barrier-peer-bound` | 5 | -3.44% | 0.10% | -3.53 pp | 26.5 | 0.0 | 0.101% | 0.00 | 0.02 |

**Result: the current run fails** in `barrier-alternating`. Failing blocks: `barrier-alternating` block 4 (+11.73 pp; run-queue delay 81.3 ms at 0% and 0.0 ms at 50%); `barrier-alternating` block 5 (-10.77 pp; run-queue delay 0.0 ms at 0% and 17.1 ms at 50%).
The gate stays unchanged. Each failing block is recorded with its run-queue delays.
A valid block has runs that all exit 0, both epochs complete with zero loss, a valid managed replay, no ledger failure, and all 10 phases complete.
Each profiled run in a valid block records at least one dependency wake. The longest handler interval in a valid block is 2,007,102 ns.

## What the skipped delay shows

In `selected-dominant`, the peer finishes each phase early and waits while the selected thread publishes credit.
Each wake skips only to the token that the selected thread posted at the barrier. The skipped delay is about half of the global delay.
Without the wake rule, the peer would owe that delay after each wake.
In `peer-bound`, the selected thread waits instead. Its wakes skip only the peer's published payment overshoot, a few hundred nanoseconds, and the prediction stays near 0%.
In `alternating`, the critical thread changes in every phase. The prediction follows the real gain of about 40%.

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261018T182448183-601598d1` | First barrier run | Pass |
| `20261018T183213218-c2268d43` | Per-thread CPU and wall time diagnostics. The prediction plan gained an open-failure note. | Pass |
| `20261018T190140317-c13b65a5` | Barrier abort, post race, and token bound | Pass |
| `20261018T191307038-5d4a0177` | Barrier errors cancel the run | Pass |
| `20261018T191852956-237f7bf8` | Blocking plan rows match the implementation | Fail. `peer-bound` block 5: the peer accepted no sample for five selected samples and broke the 2 ms debt bound. No forced GC runs here. |
| `20261018T192211666-aeadc5b5` | Run-queue delay diagnostic | Pass |
| `20261018T193110908-a2f5b436` | Run-queue delay excludes failed reads | Fail. `barrier-alternating` block 4 (+11.73 pp; run-queue delay 81.3 ms at 0% and 0.0 ms at 50%); `barrier-alternating` block 5 (-10.77 pp; run-queue delay 0.0 ms at 0% and 17.1 ms at 50%). |

Exploratory smoke runs before the criteria used other pattern names and are not part of any result.

## Limits

One host and one kernel. Five blocks for each configuration cannot establish a general error bound.
Only `TargetBarrier` uses the wait protocol. Locks, `Monitor`, tasks, and other waits stay unsupported.
A timed-out or cancelled barrier wait skips nothing. Deterministic checks cover it, but no qualified run exercises a timeout.
A worker that is preempted between its arrival and its block skips nothing for the delay inserted in that window. That biases the prediction low.
The failed run shows that a runnable thread can go without samples long enough to break the 2 ms debt bound, even without forced GC.
Each worker records its kernel run-queue delay. In this run the largest value is 17.14 ms in a 50% arm and 81.26 ms in a 0% arm.
Every worker read its run-queue delay.
Run-queue delay adds wall time that the profiler does not remove. A long delay in a 0% arm raises E0 and the prediction. A long delay in a 50% arm lowers it.

See [the blocking plan](../../../lode/plans/target-local-blocking.md) and [the API reference](../../../docsSrc/Reference/target-local-api.md).
