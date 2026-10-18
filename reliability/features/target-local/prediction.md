# Target-local prediction qualification

Evidence: `artifacts/target-local-prediction/20261018T162737768-cfc37f48`.

`TargetLocalPredictionVerify` ran once with seed 20261018 on kernel `5.15.153.1-microsoft-standard-WSL2`, x64, with 4 CPUs.
The container drops all capabilities, has no network, and uses `seccomp=unconfined`.
The acceptance criteria are in [the prediction plan](../../../lode/plans/target-local-prediction.md). The runner hashes that file with the sources.
All 16 source hashes and 3 binary hashes stay unchanged during the run.
The sample runs through the library entry point `TargetLocalProfiler.run`. `native/WriteTargetLocalEvidence.py` writes this page.

Each block runs `profile-zero`, `profile-speedup` at 50%, `original`, and `optimized` in a random order.
The prediction is `1 - Es / E0` from effective durations. The actual reduction is `1 - Toptimized / Toriginal`.

| Configuration | Selected rounds | Peer rounds | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-longer` | 6000 | 3000 | 5 / 5 | 49.77% | 50.01% | 1.05 pp | 1.36 pp | Pass |
| `peer-longer` | 3000 | 6000 | 5 / 5 | 0.99% | -0.60% | 1.69 pp | 6.94 pp | Pass |
| `selected-dominant` | 8000 | 2000 | 5 / 5 | 49.85% | 49.23% | 0.76 pp | 1.50 pp | Pass |

| Configuration | Block | Predicted | Actual | Difference | Overhead ratio | Handler share at 0% | Global delay (ns) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `selected-longer` | 1 | 50.12% | 48.76% | +1.36 pp | 1.0113 | 0.079% | 50,500,033 |
| `selected-longer` | 2 | 48.97% | 50.01% | -1.05 pp | 0.9835 | 0.075% | 51,000,024 |
| `selected-longer` | 3 | 49.77% | 50.82% | -1.05 pp | 0.9752 | 0.098% | 50,000,121 |
| `selected-longer` | 4 | 49.82% | 51.13% | -1.31 pp | 1.0034 | 0.113% | 51,000,018 |
| `selected-longer` | 5 | 48.76% | 48.93% | -0.17 pp | 1.0019 | 0.092% | 51,055,402 |
| `peer-longer` | 1 | 3.34% | -3.60% | +6.94 pp | 1.0512 | 0.100% | 26,000,032 |
| `peer-longer` | 2 | -2.09% | -6.38% | +4.29 pp | 1.0110 | 0.090% | 25,500,035 |
| `peer-longer` | 3 | 0.99% | -0.60% | +1.58 pp | 0.9977 | 0.113% | 25,500,089 |
| `peer-longer` | 4 | -0.96% | 0.30% | -1.26 pp | 1.0088 | 0.099% | 26,000,038 |
| `peer-longer` | 5 | 2.51% | 0.82% | +1.69 pp | 1.0386 | 0.092% | 25,500,023 |
| `selected-dominant` | 1 | 49.79% | 48.69% | +1.10 pp | 0.9950 | 0.075% | 67,000,000 |
| `selected-dominant` | 2 | 50.86% | 49.36% | +1.50 pp | 1.0435 | 0.068% | 66,000,000 |
| `selected-dominant` | 3 | 48.11% | 48.53% | -0.42 pp | 1.0706 | 0.090% | 68,500,000 |
| `selected-dominant` | 4 | 49.85% | 49.88% | -0.04 pp | 0.9981 | 0.067% | 67,500,000 |
| `selected-dominant` | 5 | 49.99% | 49.23% | +0.76 pp | 0.9920 | 0.076% | 66,500,000 |

The pass rule requires 5 valid blocks in each of the three configurations, a median absolute difference of at most 5 percentage points, and every block within 10.
All three configurations pass. Every profile run has complete epochs, zero loss, a valid managed replay, matching global delay and event count, and no ledger failure.
The longest handler interval in any profile run is 1,609,261 ns.

In `selected-dominant`, a 50% optimization leaves the selected thread longer than the peer. Over-credit would show as a prediction above 50%.
The median prediction is 49.85%. The highest block prediction is 50.86%, inside the block limit.
The median does not show the over-credit signature.

## Overhead

The handler share is the sum of every handler interval in both threads divided by elapsed time.
At 0% speedup the handler inserts no delay. Its share is then the measured handler time, and its median is 0.075% to 0.099% across the three configurations.
At 50% speedup the share also holds the inserted delay, so it is not overhead. Its median is 20.3% to 49.4%.
The ratio of `profile-zero` elapsed time to `original` elapsed time has medians from 0.9981 to 1.0110. That ratio also holds run-to-run variation.
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

Three exploratory blocks for each of the first two configurations ran before any qualification. They are not part of any result.

## Limits

One host and one kernel. Five blocks for each configuration cannot establish a general error bound.
The program is two CPU-bound threads with no waits before the join. Its methods allocate nothing, so GC is not exercised here.
The `actual` values also vary by several points between blocks with the same work.
Positive differences per configuration: `selected-longer` 1 of 5, `peer-longer` 4 of 5, `selected-dominant` 3 of 5. Five blocks cannot separate a small bias from variation.
The peer pays by spinning, so payment uses CPU. A host with fewer free CPUs than threads can change the result.

See [the tutorial](../../../docsSrc/Tutorials/Target_Local_Prediction.md) and [the support boundary](../../../docsSrc/Explanations/target-local-prediction.md).
