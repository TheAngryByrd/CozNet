# Target-local barrier qualification

Evidence: `artifacts/target-local-barrier/20261018T210647512-cd99aa09`.

`TargetLocalBarrierVerify` ran once with seed 20261018 on kernel `5.15.153.1-microsoft-standard-WSL2`, x64, with 4 CPUs.
The criteria are in [the prediction plan](../../../lode/plans/target-local-prediction.md#barrier-qualification), fixed before the first barrier run.
The runner hashes that file, [the blocking plan](../../../lode/plans/target-local-blocking.md), and the sources. All hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

Two registered threads run 10 phases. In each phase the selected thread runs A rounds and the peer runs B rounds. Both then meet at a `TargetBarrier`.
Each block runs `profile-phases 0`, `profile-phases 50`, `plain-phases <pattern> 100`, and `plain-phases <pattern> 50` in a random order.

| Configuration | Pattern | Expected actual | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Median skipped delay | Result |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `barrier-alternating` | `alternating` | about 40% | 5 / 5 | 39.27% | 40.25% | 1.71 pp | 2.60 pp | 51.0 ms | Pass |
| `barrier-selected-dominant` | `selected-dominant` | about 50% | 4 / 5 | 49.75% | 49.37% | 0.97 pp | 1.46 pp | 102.7 ms | Fail |
| `barrier-peer-bound` | `peer-bound` | about 0% | 5 / 5 | 1.68% | -0.28% | 1.75 pp | 4.03 pp | 0.0 ms | Pass |

| Configuration | Block | Predicted | Actual | Difference | Global delay (ms) | Skipped delay (ms) | Handler share at 0% | Run-queue delay at 0% (ms) | Run-queue delay at 50% (ms) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `barrier-alternating` | 1 | 39.27% | 40.25% | -0.98 pp | 115.6 | 51.0 | 0.083% | 0.06 | 0.46 |
| `barrier-alternating` | 2 | 39.71% | 38.46% | +1.24 pp | 118.0 | 53.5 | 0.103% | 0.01 | 0.01 |
| `barrier-alternating` | 3 | 39.75% | 41.46% | -1.71 pp | 118.0 | 50.5 | 0.108% | 0.09 | 0.14 |
| `barrier-alternating` | 4 | 38.00% | 40.60% | -2.60 pp | 115.5 | 50.5 | 0.064% | 0.02 | 0.00 |
| `barrier-alternating` | 5 | 36.54% | 38.29% | -1.75 pp | 118.0 | 53.0 | 0.076% | 0.00 | 0.00 |
| `barrier-selected-dominant` | 1 | Invalid: profile-speedup exit 1; profile-speedup not passed | | | | | | | |
| `barrier-selected-dominant` | 2 | 49.67% | 50.26% | -0.60 pp | 205.1 | 101.0 | 0.082% | 0.01 | 0.01 |
| `barrier-selected-dominant` | 3 | 49.83% | 48.48% | +1.35 pp | 210.0 | 104.5 | 0.104% | 0.00 | 0.38 |
| `barrier-selected-dominant` | 4 | 50.09% | 51.55% | -1.46 pp | 202.5 | 98.0 | 0.071% | 0.01 | 0.01 |
| `barrier-selected-dominant` | 5 | 48.61% | 48.41% | +0.20 pp | 207.0 | 105.0 | 0.075% | 0.01 | 0.55 |
| `barrier-peer-bound` | 1 | 2.17% | -0.28% | +2.45 pp | 25.5 | 0.0 | 0.088% | 0.00 | 0.00 |
| `barrier-peer-bound` | 2 | -2.56% | -1.49% | -1.07 pp | 26.0 | 0.0 | 0.090% | 0.01 | 0.00 |
| `barrier-peer-bound` | 3 | 1.75% | -2.28% | +4.03 pp | 25.6 | 0.1 | 0.145% | 0.15 | 0.00 |
| `barrier-peer-bound` | 4 | -0.78% | 0.43% | -1.21 pp | 25.5 | 0.0 | 0.125% | 0.01 | 0.00 |
| `barrier-peer-bound` | 5 | 1.68% | -0.07% | +1.75 pp | 26.5 | 0.0 | 0.231% | 0.00 | 0.00 |

**Result: the current run fails** in `barrier-selected-dominant`. Failed criteria: `barrier-selected-dominant` block 1 invalid (profile-speedup exit 1, profile-speedup not passed, ledger failure 1 in the 50% arm; run-queue delay 0.3 ms at 0% and 82.9 ms at 50%).
The gate stays unchanged. Each failing block is recorded with its run-queue delays.
A valid block has runs that all exit 0, both epochs complete with zero loss, a valid managed replay, no ledger failure, and all 10 phases complete.
Each profiled run in a valid block records at least one dependency wake. The longest handler interval in a valid block is 1,759,682 ns.

## What the skipped delay shows

In `selected-dominant`, the peer finishes each phase early and waits while the selected thread publishes credit.
Each wake skips only to the token that the selected thread posted at the barrier. The skipped delay is about half of the global delay.
Without the wake rule, the peer would owe that delay after each wake.
In `peer-bound`, the selected thread waits instead. Its wakes skip only the peer's published payment overshoot, a few hundred nanoseconds, and the prediction stays near 0%.
In `alternating`, the critical thread changes in every phase. The prediction follows the real gain of about 40%.

## SMT and run-queue diagnostic

Diagnostic run: `artifacts/target-local-barrier-cpuset/20261018T203443644-3be38dc4`, with `--cpuset-cpus 0,2`. It does not replace any qualification run.
The host CPUs 0-1 and 2-3 are SMT pairs, so that run puts one thread on each physical core.

In qualification run `20261018T203120679-31605bcf`, the `peer-bound` differences are all positive. With one thread per physical core, they are +0.0, -2.3, +3.5, +16.5, -0.4 points, with no sign bias.
The outlying block in that run comes from a slow plain `optimized` arm, not from a profiled arm.
This is consistent with the hypothesis that SMT contention changes while a thread spins to pay delay. The cpuset also halves the CPU count, so the run does not isolate SMT.
The later unrestricted qualification run also shows no sign bias in `peer-bound`. The hypothesis stays open.

The same run captures one debt failure with a run-queue correlation. In `selected-dominant` block 3, the selected thread posts a token and releases the peer.
The peer's wake runs 8.659 ms later. In that interval, the selected thread publishes 4.5 ms of credit, so the woken peer owes 4.5 ms.
The peer's run-queue delay across the whole run is 8.638 ms, close to the length of that interval. The diagnostic has no interval times, so it cannot place the delay.
A runnable peer that waited for a CPU in that interval would explain the failure.
If so, the wake rule is correct here: the credit after the post belongs to time when the peer was runnable, and a thread that does not run cannot pay.

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261018T182448183-601598d1` | First barrier run | Pass |
| `20261018T183213218-c2268d43` | Per-thread CPU and wall time diagnostics. The prediction plan gained an open-failure note. | Pass |
| `20261018T190140317-c13b65a5` | Barrier abort, post race, and token bound | Pass |
| `20261018T191307038-5d4a0177` | Barrier errors cancel the run | Pass |
| `20261018T191852956-237f7bf8` | Blocking plan rows match the implementation | Fail. `peer-bound` block 5: the peer accepted no sample for five selected samples and broke the 2 ms debt bound. No forced GC runs here. |
| `20261018T192211666-aeadc5b5` | Run-queue delay diagnostic | Pass |
| `20261018T193110908-a2f5b436` | Run-queue delay excludes failed reads | Fail. `barrier-alternating` blocks 4 and 5 differ by +11.73 and -10.77 points, with 81.3 ms and 17.1 ms of run-queue delay in one arm each. |
| `20261018T202640118-21860ac2` | Code binding enforced in every profile mode | Fail. `peer-bound` blocks 4 and 5 differ by +12.29 and +7.85 points. The run-queue diagnostic read inside the timed window raised overhead. |
| `20261018T203120679-31605bcf` | Run-queue reads outside the timed window | Fail. `peer-bound` median 5.28 pp, with every difference positive. |
| `20261018T204129945-675e5b4b` | SMT diagnostic note in the prediction plan and the `-CpuSet` option | Pass |
| `20261018T210647512-cd99aa09` | Review fixes: perf-map setting check, checked-run seam, shutdown work-state marker, and wording | Fail. `barrier-selected-dominant` block 1 invalid (profile-speedup exit 1, profile-speedup not passed, ledger failure 1 in the 50% arm; run-queue delay 0.3 ms at 0% and 82.9 ms at 50%). |

Exploratory smoke runs before the criteria used other pattern names and are not part of any result.

## Limits

One host and one kernel. Five blocks for each configuration cannot establish a general error bound.
Only `TargetBarrier` uses the wait protocol. Locks, `Monitor`, tasks, and other waits stay unsupported.
A timed-out or cancelled barrier wait skips nothing. Deterministic checks cover it, but no qualified run exercises a timeout.
A worker that is preempted between its arrival and its block skips nothing for the delay inserted in that window. That biases the prediction low.
The failed run shows that a runnable thread can go without samples long enough to break the 2 ms debt bound, even without forced GC.
Each worker records its kernel run-queue delay. In this run the largest value is 82.94 ms in a 50% arm and 0.30 ms in a 0% arm.
Every worker read its run-queue delay.
Run-queue delay adds wall time that the profiler does not remove. A long delay in a 0% arm raises E0 and the prediction. A long delay in a 50% arm lowers it.

See [the blocking plan](../../../lode/plans/target-local-blocking.md) and [the API reference](../../../docsSrc/Reference/target-local-api.md).
