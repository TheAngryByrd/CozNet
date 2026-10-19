# Target-local barrier qualification

Evidence: `artifacts/target-local-barrier/20261019T070924325-199df270`.

`TargetLocalBarrierVerify` ran once with seed 20261018 on kernel `5.15.153.1-microsoft-standard-WSL2`, x64, with 4 CPUs.
The criteria are in [the prediction plan](../../../lode/plans/target-local-prediction.md#barrier-qualification), fixed before the first barrier run.
The runner hashes that file, [the blocking plan](../../../lode/plans/target-local-blocking.md), and the sources. All hashes stay unchanged.
`native/WriteTargetLocalEvidence.py` writes this page.

Two registered threads run 10 phases. In each phase the selected thread runs A rounds and the peer runs B rounds. Both then meet at a `TargetBarrier`.
Each block runs `profile-phases 0`, `profile-phases 50`, `plain-phases <pattern> 100`, and `plain-phases <pattern> 50` in a random order.

| Configuration | Pattern | Expected actual | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Median skipped delay | Result |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `barrier-alternating` | `alternating` | about 40% | 5 / 5 | 40.05% | 40.82% | 2.34 pp | 4.31 pp | 51.5 ms | Pass |
| `barrier-selected-dominant` | `selected-dominant` | about 50% | 5 / 5 | 49.16% | 49.05% | 0.59 pp | 1.93 pp | 101.5 ms | Pass |
| `barrier-peer-bound` | `peer-bound` | about 0% | 5 / 5 | -1.00% | 2.17% | 2.39 pp | 8.59 pp | 0.0 ms | Pass |

| Configuration | Block | Predicted | Actual | Difference | Global delay (ms) | Skipped delay (ms) | Handler share at 0% | Run-queue delay at 0% (ms) | Run-queue delay at 50% (ms) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `barrier-alternating` | 1 | 40.38% | 37.48% | +2.91 pp | 113.5 | 50.5 | 0.068% | 0.00 | 0.01 |
| `barrier-alternating` | 2 | 40.05% | 41.49% | -1.44 pp | 114.0 | 51.5 | 0.074% | 0.00 | 1.56 |
| `barrier-alternating` | 3 | 38.65% | 40.82% | -2.17 pp | 115.0 | 51.0 | 0.059% | 0.01 | 0.00 |
| `barrier-alternating` | 4 | 39.13% | 43.44% | -4.31 pp | 115.5 | 51.5 | 0.090% | 0.01 | 0.05 |
| `barrier-alternating` | 5 | 40.65% | 38.31% | +2.34 pp | 113.5 | 52.0 | 0.090% | 0.87 | 0.01 |
| `barrier-selected-dominant` | 1 | 49.16% | 48.57% | +0.59 pp | 209.0 | 103.0 | 0.084% | 0.02 | 0.79 |
| `barrier-selected-dominant` | 2 | 48.81% | 47.13% | +1.68 pp | 206.5 | 102.0 | 0.066% | 0.02 | 0.12 |
| `barrier-selected-dominant` | 3 | 49.64% | 49.05% | +0.59 pp | 203.5 | 100.5 | 0.070% | 0.06 | 0.01 |
| `barrier-selected-dominant` | 4 | 50.53% | 50.21% | +0.32 pp | 202.5 | 99.0 | 0.083% | 0.00 | 0.07 |
| `barrier-selected-dominant` | 5 | 48.46% | 50.39% | -1.93 pp | 203.5 | 101.5 | 0.065% | 0.01 | 0.00 |
| `barrier-peer-bound` | 1 | 0.13% | -2.26% | +2.39 pp | 25.0 | 0.0 | 0.108% | 0.00 | 0.00 |
| `barrier-peer-bound` | 2 | -2.64% | 5.96% | -8.59 pp | 25.5 | 0.0 | 0.083% | 0.01 | 0.01 |
| `barrier-peer-bound` | 3 | -5.74% | 2.17% | -7.91 pp | 25.5 | 0.0 | 0.098% | 0.00 | 0.07 |
| `barrier-peer-bound` | 4 | -1.00% | 0.56% | -1.56 pp | 25.0 | 0.0 | 0.107% | 0.00 | 0.00 |
| `barrier-peer-bound` | 5 | 3.19% | 2.33% | +0.86 pp | 26.0 | 0.0 | 0.130% | 0.00 | 0.00 |

**Result: the current run passes.**
The gate stays unchanged. Each failing block is recorded with its run-queue delays.
A valid block has runs that all exit 0, both epochs complete with zero loss, a valid managed replay, no ledger failure, and all 10 phases complete.
Each profiled run in a valid block records at least one dependency wake. The longest handler interval in a valid block is 1,593,169 ns.

## What the skipped delay shows

In `selected-dominant`, the peer finishes each phase early and waits while the selected thread publishes credit.
Each wake skips only to the token that the selected thread posted at the barrier. The skipped delay is about half of the global delay.
Without the wake rule, the peer would owe that delay after each wake.
In `peer-bound`, the selected thread waits instead. Its wakes skip only the peer's published payment overshoot, a few hundred nanoseconds, and the prediction stays near 0%.
In `alternating`, the critical thread changes in every phase. The prediction follows the real gain of about 40%.

## Run-queue delay

The wait records are complete: each profile arm must hold one value for each wake, with none dropped. The readable wait values sum to 0.58 ms across the run. The start trace is complete. Across all profile arms, the largest native start to first point is 0.0192 ms, the largest setup segment is 0.0067 ms, with a median of 0.0043 ms, and the largest barrier segment is 0.1794 ms. The start records are complete: each profile thread must hold a start run-queue delay and a work start time in the order of its native start and finish.

No profile arm records a debt failure.

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
| `20261018T210647512-cd99aa09` | Review fixes: perf-map setting check, checked-run seam, shutdown work-state marker, and wording | Fail. `barrier-selected-dominant` block 1 invalid: a 5 ms debt failure at the peer's first sample, with 82.9 ms of whole-run run-queue delay. |
| `20261018T214145452-1972f479` | Per-wait run-queue delay, pread reader, and LF-normalized source hashes, before review fixes | Fail. `peer-bound` block 5 +12.18 pp. Its arms vary by about 7%, with at most 0.34 ms of run-queue delay in the profiled arms. |
| `20261018T221415876-73afdb27` | Review fixes: wait copy after the finish time, scalar parser, debt-exceeded and dependency-wake pairing | Fail. `barrier-alternating` block 3 invalid: a 3 ms debt failure at the peer's first sample, before its first wake, with 6.7 ms of whole-run run-queue delay. |
| `20261018T234001547-747aae56` | Start run-queue delay diagnostic | Pass |
| `20261019T001733954-3bae192a` | Start trace, with on-CPU time from schedstat field 1, before the revision | Pass |
| `20261019T002531212-f435da11` | Start trace with the thread CPU clock | Fail. Criterion 14: all 300 wait values are zero. All 15 blocks are within the limits, and 27 of 60 whole-run delays are above zero. |
| `20261019T004925668-914e6b56` | Thread state set before the native start, criterion 27 for barrier runs | Pass |
| `20261019T011713329-52b0bbca` | Forced-GC windows | Pass |
| `20261019T014236242-6440bd58` | ReJIT detection check | Pass |
| `20261019T020204625-4ef1deb4` | Linux backend file split and lifecycle plan update | Pass |
| `20261019T024401530-b69ae382` | Causal profile driver | Fail. Criterion 26: one of 60 setup segments takes 0.70 ms, with 9.3 microseconds on a CPU. All 15 blocks are within the limits. |
| `20261019T032421198-d2441fed` | Throughput progress points | Pass |
| `20261019T040754718-d668c4c1` | Blocking queue and pipeline profile | Pass |
| `20261019T044024104-2513fe0c` | Per-thread experiment diagnostics | Fail. `barrier-selected-dominant` block 2 invalid: a debt failure 2 microseconds after a wake whose wait has 7.40 ms of run-queue delay. |
| `20261019T051353871-0dc405c5` | Profile warm-up experiment | Pass |
| `20261019T060913007-5812aec4` | Rotated experiment order and warm-up gate | Pass |
| `20261019T063253419-973e8541` | TargetLock and lock profile | Pass |
| `20261019T070924325-199df270` | Lock handoff counts | Pass |

Exploratory smoke runs before the criteria used other pattern names and are not part of any result.

## Limits

One host and one kernel. Five blocks for each configuration cannot establish a general error bound.
Only `TargetBarrier` uses the wait protocol. Locks, `Monitor`, tasks, and other waits stay unsupported.
A timed-out or cancelled barrier wait skips nothing. Deterministic checks cover it, but no qualified run exercises a timeout.
A worker that is preempted between its arrival and its block skips nothing for the delay inserted in that window. That biases the prediction low.
The failed run shows that a runnable thread can go without samples long enough to break the 2 ms debt bound, even without forced GC.
Each worker records its kernel run-queue delay. In this run the largest value is 1.56 ms in a 50% arm and 0.87 ms in a 0% arm.
Every worker read its run-queue delay.
Run-queue delay adds wall time that the profiler does not remove. A long delay in a 0% arm raises E0 and the prediction. A long delay in a 50% arm lowers it.

See [the blocking plan](../../../lode/plans/target-local-blocking.md) and [the API reference](../../../docsSrc/Reference/target-local-api.md).
