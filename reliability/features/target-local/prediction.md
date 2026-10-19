# Target-local prediction qualification

Evidence: `artifacts/target-local-prediction/20261019T114345641-a76a3c68`.

`TargetLocalPredictionVerify` ran once with seed 20261018 on kernel `5.15.153.1-microsoft-standard-WSL2`, x64, with 4 CPUs.
The container drops all capabilities, has no network, and uses `seccomp=unconfined`.
The acceptance criteria are in [the prediction plan](../../../lode/plans/target-local-prediction.md). The runner hashes that file with the sources.
All 24 source hashes and 3 binary hashes stay unchanged during the run.
The sample runs through the checked library entry point `TargetLocalProfiler.runBound` with a `CodeBinding`. `native/WriteTargetLocalEvidence.py` writes this page.

Each block runs `profile-zero`, `profile-speedup` at 50%, `original`, and `optimized` in a random order.
The prediction is `1 - Es / E0` from effective durations. The actual reduction is `1 - Toptimized / Toriginal`.

| Configuration | Selected rounds | Peer rounds | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-longer` | 6000 | 3000 | 5 / 5 | 48.43% | 49.49% | 1.02 pp | 4.51 pp | Pass |
| `peer-longer` | 3000 | 6000 | 5 / 5 | -0.42% | 0.80% | 1.55 pp | 3.73 pp | Pass |
| `selected-dominant` | 8000 | 2000 | 5 / 5 | 50.05% | 48.43% | 1.70 pp | 3.59 pp | Pass |

| Configuration | Block | Predicted | Actual | Difference | Overhead ratio | Handler share at 0% | Global delay (ns) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `selected-longer` | 1 | 48.44% | 49.46% | -1.02 pp | 1.0165 | 0.099% | 52,000,002 |
| `selected-longer` | 2 | 48.43% | 49.49% | -1.06 pp | 0.9758 | 0.165% | 52,500,000 |
| `selected-longer` | 3 | 49.30% | 49.95% | -0.65 pp | 0.9847 | 0.163% | 51,500,021 |
| `selected-longer` | 4 | 47.47% | 47.47% | +0.00 pp | 1.0119 | 0.095% | 51,500,005 |
| `selected-longer` | 5 | 47.77% | 52.28% | -4.51 pp | 0.9555 | 0.105% | 51,500,001 |
| `peer-longer` | 1 | 1.78% | 0.45% | +1.33 pp | 1.0137 | 0.221% | 25,500,013 |
| `peer-longer` | 2 | -0.42% | 0.83% | -1.25 pp | 0.9749 | 0.095% | 25,500,109 |
| `peer-longer` | 3 | -0.75% | 0.80% | -1.55 pp | 1.0155 | 0.092% | 25,500,018 |
| `peer-longer` | 4 | 2.42% | -1.31% | +3.73 pp | 1.0141 | 0.185% | 25,500,031 |
| `peer-longer` | 5 | -0.96% | 2.74% | -3.70 pp | 0.9754 | 0.124% | 25,000,005 |
| `selected-dominant` | 1 | 49.82% | 48.27% | +1.54 pp | 1.0206 | 0.077% | 68,000,000 |
| `selected-dominant` | 2 | 47.69% | 50.60% | -2.91 pp | 0.9889 | 0.081% | 70,000,000 |
| `selected-dominant` | 3 | 50.19% | 50.04% | +0.15 pp | 1.0301 | 0.127% | 69,500,000 |
| `selected-dominant` | 4 | 50.05% | 48.35% | +1.70 pp | 1.0111 | 0.074% | 67,500,000 |
| `selected-dominant` | 5 | 52.03% | 48.43% | +3.59 pp | 1.0627 | 0.217% | 68,000,000 |

The pass rule requires 5 valid blocks in each of the three configurations, a median absolute difference of at most 5 percentage points, and every block within 10.
All three configurations pass. Every profile run has complete epochs, zero loss, a valid managed replay, matching global delay and event count, and no ledger failure.
The longest handler interval in any profile run of a valid block is 1,582,407 ns.

In `selected-dominant`, a 50% optimization leaves the selected thread longer than the peer. Over-credit would show as a prediction above 50%.
The median prediction is 50.05%. The highest block prediction is 52.03%. Every block is inside the block limit.
A median above 50% is the over-credit signature. This run shows it, and five blocks cannot separate it from variation.

## Run-queue delay

The wait records are complete: each profile arm must hold one value for each wake, with none dropped. The readable wait values sum to 0.00 ms across the run. The start trace is complete. Across all profile arms, the largest native start to first point is 0.0280 ms, the largest setup segment is 0.0062 ms, with a median of 0.0050 ms, and the largest barrier segment is 0.1719 ms. The start records are complete: each profile thread must hold a start run-queue delay and a work start time in the order of its native start and finish.

No profile arm records a debt failure.

## Overhead

The handler share is the sum of every handler interval in both threads divided by elapsed time.
At 0% speedup the handler inserts no delay. Its share is then the measured handler time, and its median is 0.081% to 0.124% across the three configurations.
At 50% speedup the share also holds the inserted delay, so it is not overhead. Its median is 19.8% to 49.1%.
The ratio of `profile-zero` elapsed time to `original` elapsed time has medians from 0.9847 to 1.0206. That ratio also holds run-to-run variation.
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
| `20261019T020044104-92e8b8b1` | Same | Linux backend file split and lifecycle plan update | Pass |
| `20261019T024239257-fae8a160` | Same | Causal profile driver | Pass |
| `20261019T032259730-367c4746` | Same | Throughput progress points | Pass |
| `20261019T040632196-702f8052` | Same | Blocking queue and pipeline profile | Pass |
| `20261019T043903739-5859c385` | Same | Per-thread experiment diagnostics | Pass |
| `20261019T051231424-1e546bce` | Same | Profile warm-up experiment | Pass |
| `20261019T060749814-6eba8ca5` | Same | Rotated experiment order and warm-up gate | Pass |
| `20261019T063130911-86311728` | Same | TargetLock and lock profile | Pass |
| `20261019T070800008-220def3b` | Same | Lock handoff counts | Fail. `peer-longer` has 4 valid blocks. In block 4, the peer breaks the 2 ms debt bound 1.38 ms after its work start, with 3.75 ms of start run-queue delay. |
| `20261019T075004985-14232a40` | Same | Spin before a lock wait | Pass |
| `20261019T082954122-98c31e2d` | Same | TargetSemaphore and semaphore profile | Pass |
| `20261019T091027391-fbd17a7d` | Same | Failure and GC records | Pass |
| `20261019T094506888-d9c14620` | Same | Failure context | Fail. `peer-longer` has one block of -12.57 points, and `selected-dominant` block 5 is invalid from a startup debt failure. |
| `20261019T110837795-78ce6bcb` | Same | Latency progress points | Pass |
| `20261019T114345641-a76a3c68` | Same | Per-transaction virtual latency | Pass |

Three exploratory blocks for each of the first two configurations ran before any qualification. They are not part of any result.

## Limits

One host and one kernel. Five blocks for each configuration cannot establish a general error bound.
The program is two CPU-bound threads with no waits before the join. Its methods allocate nothing, so GC is not exercised here.
The `actual` values also vary by several points between blocks with the same work.
Positive differences per configuration: `selected-longer` 1 of 5, `peer-longer` 2 of 5, `selected-dominant` 4 of 5. Five blocks cannot separate a small bias from variation.
The peer pays by spinning, so payment uses CPU. A host with fewer free CPUs than threads can change the result.

See [the tutorial](../../../docsSrc/Tutorials/Target_Local_Prediction.md) and [the support boundary](../../../docsSrc/Explanations/target-local-prediction.md).
