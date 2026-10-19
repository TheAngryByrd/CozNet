# Target-local prediction qualification

Evidence: `artifacts/target-local-prediction/20261019T014112845-eec890b5`.

`TargetLocalPredictionVerify` ran once with seed 20261018 on kernel `5.15.153.1-microsoft-standard-WSL2`, x64, with 4 CPUs.
The container drops all capabilities, has no network, and uses `seccomp=unconfined`.
The acceptance criteria are in [the prediction plan](../../../lode/plans/target-local-prediction.md). The runner hashes that file with the sources.
All 22 source hashes and 3 binary hashes stay unchanged during the run.
The sample runs through the checked library entry point `TargetLocalProfiler.runBound` with a `CodeBinding`. `native/WriteTargetLocalEvidence.py` writes this page.

Each block runs `profile-zero`, `profile-speedup` at 50%, `original`, and `optimized` in a random order.
The prediction is `1 - Es / E0` from effective durations. The actual reduction is `1 - Toptimized / Toriginal`.

| Configuration | Selected rounds | Peer rounds | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-longer` | 6000 | 3000 | 5 / 5 | 48.48% | 49.23% | 0.75 pp | 0.88 pp | Pass |
| `peer-longer` | 3000 | 6000 | 5 / 5 | 1.63% | -0.27% | 2.79 pp | 3.31 pp | Pass |
| `selected-dominant` | 8000 | 2000 | 5 / 5 | 49.04% | 50.35% | 1.68 pp | 2.69 pp | Pass |

| Configuration | Block | Predicted | Actual | Difference | Overhead ratio | Handler share at 0% | Global delay (ns) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `selected-longer` | 1 | 48.48% | 49.23% | -0.75 pp | 0.9864 | 0.134% | 52,500,021 |
| `selected-longer` | 2 | 48.29% | 48.78% | -0.49 pp | 1.0127 | 0.193% | 53,000,041 |
| `selected-longer` | 3 | 49.92% | 49.37% | +0.55 pp | 1.0233 | 0.106% | 50,500,013 |
| `selected-longer` | 4 | 48.23% | 49.11% | -0.88 pp | 1.0383 | 0.133% | 53,000,054 |
| `selected-longer` | 5 | 49.66% | 50.48% | -0.81 pp | 0.9999 | 0.143% | 51,500,022 |
| `peer-longer` | 1 | 3.01% | 2.37% | +0.64 pp | 1.0057 | 0.147% | 25,500,041 |
| `peer-longer` | 2 | -2.26% | 1.06% | -3.31 pp | 0.9819 | 0.109% | 26,500,081 |
| `peer-longer` | 3 | 1.63% | -1.17% | +2.79 pp | 0.9873 | 0.115% | 26,500,023 |
| `peer-longer` | 4 | 2.61% | -0.27% | +2.88 pp | 1.0221 | 0.106% | 25,000,013 |
| `peer-longer` | 5 | -1.60% | -0.45% | -1.16 pp | 0.9533 | 0.087% | 25,500,039 |
| `selected-dominant` | 1 | 49.04% | 49.45% | -0.41 pp | 0.9784 | 0.082% | 67,500,000 |
| `selected-dominant` | 2 | 50.51% | 50.66% | -0.15 pp | 1.0092 | 0.087% | 68,000,000 |
| `selected-dominant` | 3 | 48.66% | 50.98% | -2.32 pp | 0.9543 | 0.085% | 68,000,000 |
| `selected-dominant` | 4 | 50.45% | 48.77% | +1.68 pp | 1.0112 | 0.094% | 69,000,000 |
| `selected-dominant` | 5 | 47.65% | 50.35% | -2.69 pp | 0.9753 | 0.077% | 70,000,000 |

The pass rule requires 5 valid blocks in each of the three configurations, a median absolute difference of at most 5 percentage points, and every block within 10.
All three configurations pass. Every profile run has complete epochs, zero loss, a valid managed replay, matching global delay and event count, and no ledger failure.
The longest handler interval in any profile run of a valid block is 1,510,525 ns.

In `selected-dominant`, a 50% optimization leaves the selected thread longer than the peer. Over-credit would show as a prediction above 50%.
The median prediction is 49.04%. The highest block prediction is 50.51%. Every block is inside the block limit.
The median does not show the over-credit signature.

## Run-queue delay

The wait records are complete: each profile arm must hold one value for each wake, with none dropped. The readable wait values sum to 0.00 ms across the run. The start trace is complete. Across all profile arms, the largest native start to first point is 0.1076 ms, the largest setup segment is 0.0162 ms, with a median of 0.0050 ms, and the largest barrier segment is 0.1306 ms. The start records are complete: each profile thread must hold a start run-queue delay and a work start time in the order of its native start and finish.

No profile arm records a debt failure.

## Overhead

The handler share is the sum of every handler interval in both threads divided by elapsed time.
At 0% speedup the handler inserts no delay. Its share is then the measured handler time, and its median is 0.085% to 0.134% across the three configurations.
At 50% speedup the share also holds the inserted delay, so it is not overhead. Its median is 20.2% to 49.4%.
The ratio of `profile-zero` elapsed time to `original` elapsed time has medians from 0.9784 to 1.0127. That ratio also holds run-to-run variation.
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
| `20261018T214023824-28b274dc` | Same | Per-wait run-queue delay, pread reader, and LF-normalized source hashes, before review fixes | Fail. `peer-longer` block 1 +16.79 pp: its plain `optimized` arm ran 118.5 ms against 104.1 ms for `original`. |
| `20261018T221254076-9a1e3880` | Same | Review fixes: wait copy after the finish time, scalar parser, debt-exceeded and dependency-wake pairing | Pass |
| `20261018T233840885-22a9d378` | Same | Start run-queue delay diagnostic | Fail. `selected-dominant` block 5 invalid: the peer started work 5.3 ms late, with 8.3 microseconds of start run-queue delay, and its first sample recorded a 3 ms debt failure. |
| `20261019T001613652-a3f06d98` | Same | Start trace, with on-CPU time from schedstat field 1, before the revision | Pass |
| `20261019T002411461-b833589f` | Same | Start trace with the thread CPU clock | Pass |
| `20261019T004805873-c97ab2e5` | Same | Thread state set before the native start, criterion 27 for barrier runs | Fail. Criterion 26: one of 60 setup segments takes 0.60 ms, with a median of 0.0049 ms. All 15 blocks are within the limits. |
| `20261019T011552600-3ac91378` | Same | Forced-GC windows | Pass |
| `20261019T014112845-eec890b5` | Same | ReJIT detection check | Pass |

Three exploratory blocks for each of the first two configurations ran before any qualification. They are not part of any result.

## Limits

One host and one kernel. Five blocks for each configuration cannot establish a general error bound.
The program is two CPU-bound threads with no waits before the join. Its methods allocate nothing, so GC is not exercised here.
The `actual` values also vary by several points between blocks with the same work.
Positive differences per configuration: `selected-longer` 1 of 5, `peer-longer` 3 of 5, `selected-dominant` 1 of 5. Five blocks cannot separate a small bias from variation.
The peer pays by spinning, so payment uses CPU. A host with fewer free CPUs than threads can change the result.

See [the tutorial](../../../docsSrc/Tutorials/Target_Local_Prediction.md) and [the support boundary](../../../docsSrc/Explanations/target-local-prediction.md).
