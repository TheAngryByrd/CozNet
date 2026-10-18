# Target-local prediction qualification

Evidence: `artifacts/target-local-prediction/20261018T210529693-6820c454`.

`TargetLocalPredictionVerify` ran once with seed 20261018 on kernel `5.15.153.1-microsoft-standard-WSL2`, x64, with 4 CPUs.
The container drops all capabilities, has no network, and uses `seccomp=unconfined`.
The acceptance criteria are in [the prediction plan](../../../lode/plans/target-local-prediction.md). The runner hashes that file with the sources.
All 16 source hashes and 3 binary hashes stay unchanged during the run.
The sample runs through the checked library entry point `TargetLocalProfiler.runBound` with a `CodeBinding`. `native/WriteTargetLocalEvidence.py` writes this page.

Each block runs `profile-zero`, `profile-speedup` at 50%, `original`, and `optimized` in a random order.
The prediction is `1 - Es / E0` from effective durations. The actual reduction is `1 - Toptimized / Toriginal`.

| Configuration | Selected rounds | Peer rounds | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-longer` | 6000 | 3000 | 5 / 5 | 49.41% | 49.74% | 1.26 pp | 1.81 pp | Pass |
| `peer-longer` | 3000 | 6000 | 5 / 5 | -3.79% | 2.13% | 5.69 pp | 7.03 pp | Fail |
| `selected-dominant` | 8000 | 2000 | 5 / 5 | 47.91% | 48.53% | 1.15 pp | 2.14 pp | Pass |

| Configuration | Block | Predicted | Actual | Difference | Overhead ratio | Handler share at 0% | Global delay (ns) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `selected-longer` | 1 | 49.19% | 50.20% | -1.01 pp | 1.0099 | 0.096% | 50,500,117 |
| `selected-longer` | 2 | 50.40% | 49.74% | +0.66 pp | 1.0347 | 0.191% | 52,000,024 |
| `selected-longer` | 3 | 49.41% | 50.67% | -1.26 pp | 1.0212 | 0.127% | 52,500,000 |
| `selected-longer` | 4 | 47.91% | 49.72% | -1.81 pp | 0.9930 | 0.082% | 51,000,000 |
| `selected-longer` | 5 | 50.61% | 48.92% | +1.69 pp | 1.0844 | 0.106% | 51,000,000 |
| `peer-longer` | 1 | -2.75% | 2.93% | -5.69 pp | 0.9859 | 0.102% | 26,000,006 |
| `peer-longer` | 2 | -3.79% | 2.13% | -5.93 pp | 0.9950 | 0.090% | 26,000,040 |
| `peer-longer` | 3 | -0.25% | 3.46% | -3.71 pp | 0.9743 | 0.082% | 25,000,042 |
| `peer-longer` | 4 | -6.94% | 0.08% | -7.03 pp | 0.9950 | 0.082% | 26,500,040 |
| `peer-longer` | 5 | -4.63% | -2.37% | -2.27 pp | 0.9968 | 0.084% | 25,000,020 |
| `selected-dominant` | 1 | 47.91% | 49.81% | -1.90 pp | 0.9908 | 0.073% | 68,000,000 |
| `selected-dominant` | 2 | 45.27% | 47.41% | -2.14 pp | 0.9923 | 0.072% | 72,500,000 |
| `selected-dominant` | 3 | 47.38% | 48.53% | -1.15 pp | 0.9955 | 0.065% | 69,500,000 |
| `selected-dominant` | 4 | 48.85% | 48.10% | +0.75 pp | 1.0139 | 0.070% | 67,500,000 |
| `selected-dominant` | 5 | 50.44% | 49.57% | +0.86 pp | 1.0336 | 0.097% | 67,500,000 |

The pass rule requires 5 valid blocks in each of the three configurations, a median absolute difference of at most 5 percentage points, and every block within 10.
**Result: the current run fails.** `peer-longer` 5 valid blocks, median 5.69 pp, block differences -5.7, -5.9, -3.7, -7.0, -2.3. The run is kept, and no rerun replaces it.
Every profile run still has complete epochs, zero loss, a valid managed replay, matching global delay and event count, and no ledger failure.
The longest handler interval in any profile run of a valid block is 2,032,538 ns.

In `selected-dominant`, a 50% optimization leaves the selected thread longer than the peer. Over-credit would show as a prediction above 50%.
The median prediction is 47.91%. The highest block prediction is 50.44%. Every block is inside the block limit.
The median does not show the over-credit signature.

## Overhead

The handler share is the sum of every handler interval in both threads divided by elapsed time.
At 0% speedup the handler inserts no delay. Its share is then the measured handler time, and its median is 0.072% to 0.106% across the three configurations.
At 50% speedup the share also holds the inserted delay, so it is not overhead. Its median is 20.2% to 48.8%.
The ratio of `profile-zero` elapsed time to `original` elapsed time has medians from 0.9950 to 1.0212. That ratio also holds run-to-run variation.
The handler interval excludes signal entry, exit, and kernel work. It can include descheduling inside the handler.
Neither value covers process startup, perf-map writing, or journal copying after the work.

## Run history

Every qualification run is kept. Each change to a hashed source caused one rerun under the current criteria.

| Run | Criteria | Change before the run | Result |
| --- | --- | --- | --- |
| `20261018T135308886-ecb06eba` | Two configurations | First run | Pass. Later found to time its arms from different barriers. |
| `20261018T145436535-35f0e9cb` | Three configurations | Same barrier for both arms | Pass |
| `20261018T150410931-cfa294a1` | Earlier thread start | Earlier start for both arms | Pass |
| `20261018T151318850-8709b5a5` | Same | Forced-GC mode in the sample | Pass |
| `20261018T153621123-8e2e2300` | Same | Work-window counter in the sample | Pass |
| `20261018T154921544-cbc3c976` | Handler share added | Profiler moved into the library | Fail. Every profile arm stopped on a perf-map parser defect before profiling. |
| `20261018T155143041-1a9300c5` | Same | Parser accepts the `0x` prefix | Pass |
| `20261018T161755678-2da1482f` | Same | Cleanup on failure, native layout check, and payment address check | Pass |
| `20261018T162737768-cfc37f48` | Same | Join retry, construction before open, and transition host record | Pass |
| `20261018T182717084-bb88b1d8` | Same | Wait protocol in the ledger and the library | Fail. One `peer-longer` block differs by 12.75 points. Its profiled arm ran 11 ms longer than its siblings. |
| `20261018T183034093-0968049a` | Same | Per-thread CPU and wall time diagnostics | Pass |
| `20261018T190000743-76bbee08` | Same | Barrier abort, post race, and token bound | Pass |
| `20261018T191125911-68e03b4b` | Same | Barrier errors cancel the run | Pass |
| `20261018T192337237-ab4134f4` | Same | Run-queue delay diagnostic | Pass. Median overhead ratio rose to about 1.05. |
| `20261018T193237454-cc14a621` | Same | Run-queue delay excludes failed reads | Pass. Median overhead ratio about 1.06 to 1.08. |
| `20261018T202523516-b9c53a5a` | Same | Code binding enforced in every profile mode | Pass. Median overhead ratio about 1.05 to 1.09. |
| `20261018T203002656-dab28501` | Same | Run-queue reads outside the timed window | Pass. Median overhead ratio about 0.99 to 1.02. |
| `20261018T204012284-1a60b3d6` | Same | SMT diagnostic note in the plan | Pass |
| `20261018T210529693-6820c454` | Same | Review fixes: perf-map setting check, checked-run seam, shutdown work-state marker, and wording | Fail. `peer-longer` 5 valid blocks, median 5.69 pp, block differences -5.7, -5.9, -3.7, -7.0, -2.3. |

Three exploratory blocks for each of the first two configurations ran before any qualification. They are not part of any result.

## Limits

One host and one kernel. Five blocks for each configuration cannot establish a general error bound.
The program is two CPU-bound threads with no waits before the join. Its methods allocate nothing, so GC is not exercised here.
The `actual` values also vary by several points between blocks with the same work.
Positive differences per configuration: `selected-longer` 2 of 5, `peer-longer` 0 of 5, `selected-dominant` 2 of 5. Five blocks cannot separate a small bias from variation.
The peer pays by spinning, so payment uses CPU. A host with fewer free CPUs than threads can change the result.

See [the tutorial](../../../docsSrc/Tutorials/Target_Local_Prediction.md) and [the support boundary](../../../docsSrc/Explanations/target-local-prediction.md).
