# Target-local prediction qualification

Evidence: `artifacts/target-local-prediction/20261018T193237454-cc14a621`.

`TargetLocalPredictionVerify` ran once with seed 20261018 on kernel `5.15.153.1-microsoft-standard-WSL2`, x64, with 4 CPUs.
The container drops all capabilities, has no network, and uses `seccomp=unconfined`.
The acceptance criteria are in [the prediction plan](../../../lode/plans/target-local-prediction.md). The runner hashes that file with the sources.
All 16 source hashes and 3 binary hashes stay unchanged during the run.
The sample runs through the library entry point `TargetLocalProfiler.run`. `native/WriteTargetLocalEvidence.py` writes this page.

Each block runs `profile-zero`, `profile-speedup` at 50%, `original`, and `optimized` in a random order.
The prediction is `1 - Es / E0` from effective durations. The actual reduction is `1 - Toptimized / Toriginal`.

| Configuration | Selected rounds | Peer rounds | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-longer` | 6000 | 3000 | 5 / 5 | 45.60% | 49.22% | 2.95 pp | 8.63 pp | Pass |
| `peer-longer` | 3000 | 6000 | 5 / 5 | 1.82% | 0.09% | 1.70 pp | 4.62 pp | Pass |
| `selected-dominant` | 8000 | 2000 | 5 / 5 | 47.97% | 49.96% | 1.81 pp | 5.93 pp | Pass |

| Configuration | Block | Predicted | Actual | Difference | Overhead ratio | Handler share at 0% | Global delay (ns) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `selected-longer` | 1 | 43.45% | 52.08% | -8.63 pp | 1.0070 | 0.093% | 52,500,125 |
| `selected-longer` | 2 | 45.60% | 48.55% | -2.95 pp | 1.0612 | 0.096% | 51,000,140 |
| `selected-longer` | 3 | 44.83% | 49.22% | -4.39 pp | 1.0812 | 0.110% | 52,000,049 |
| `selected-longer` | 4 | 49.48% | 48.52% | +0.96 pp | 1.1261 | 0.160% | 50,500,080 |
| `selected-longer` | 5 | 47.84% | 50.33% | -2.49 pp | 1.0783 | 0.119% | 52,000,041 |
| `peer-longer` | 1 | -5.99% | -1.37% | -4.62 pp | 1.0419 | 0.091% | 26,500,043 |
| `peer-longer` | 2 | 1.82% | 0.40% | +1.42 pp | 1.0985 | 0.141% | 25,500,081 |
| `peer-longer` | 3 | 2.72% | -1.11% | +3.83 pp | 1.1120 | 0.158% | 26,000,126 |
| `peer-longer` | 4 | -1.27% | 0.09% | -1.36 pp | 1.0215 | 0.110% | 25,500,130 |
| `peer-longer` | 5 | 3.00% | 1.30% | +1.70 pp | 1.0566 | 0.107% | 24,500,002 |
| `selected-dominant` | 1 | 48.47% | 49.96% | -1.50 pp | 1.0625 | 0.101% | 69,500,000 |
| `selected-dominant` | 2 | 46.42% | 52.35% | -5.93 pp | 1.0598 | 0.106% | 70,500,000 |
| `selected-dominant` | 3 | 49.04% | 49.78% | -0.73 pp | 1.0632 | 0.142% | 65,500,000 |
| `selected-dominant` | 4 | 47.90% | 50.05% | -2.15 pp | 1.0790 | 0.132% | 70,000,000 |
| `selected-dominant` | 5 | 47.97% | 49.77% | -1.81 pp | 1.0706 | 0.125% | 69,500,000 |

The pass rule requires 5 valid blocks in each of the three configurations, a median absolute difference of at most 5 percentage points, and every block within 10.
All three configurations pass. Every profile run has complete epochs, zero loss, a valid managed replay, matching global delay and event count, and no ledger failure.
The longest handler interval in any profile run is 1,505,745 ns.

In `selected-dominant`, a 50% optimization leaves the selected thread longer than the peer. Over-credit would show as a prediction above 50%.
The median prediction is 47.97%. The highest block prediction is 49.04%, inside the block limit.
The median does not show the over-credit signature.

## Overhead

The handler share is the sum of every handler interval in both threads divided by elapsed time.
At 0% speedup the handler inserts no delay. Its share is then the measured handler time, and its median is 0.110% to 0.125% across the three configurations.
At 50% speedup the share also holds the inserted delay, so it is not overhead. Its median is 19.1% to 46.5%.
The ratio of `profile-zero` elapsed time to `original` elapsed time has medians from 1.0566 to 1.0783. That ratio also holds run-to-run variation.
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
| `20261018T192337237-ab4134f4` | Same | Run-queue delay diagnostic | Pass |
| `20261018T193237454-cc14a621` | Same | Run-queue delay excludes failed reads | Pass |

Three exploratory blocks for each of the first two configurations ran before any qualification. They are not part of any result.

## Limits

One host and one kernel. Five blocks for each configuration cannot establish a general error bound.
The program is two CPU-bound threads with no waits before the join. Its methods allocate nothing, so GC is not exercised here.
The `actual` values also vary by several points between blocks with the same work.
Positive differences per configuration: `selected-longer` 1 of 5, `peer-longer` 3 of 5, `selected-dominant` 0 of 5. Five blocks cannot separate a small bias from variation.
The peer pays by spinning, so payment uses CPU. A host with fewer free CPUs than threads can change the result.

See [the tutorial](../../../docsSrc/Tutorials/Target_Local_Prediction.md) and [the support boundary](../../../docsSrc/Explanations/target-local-prediction.md).
