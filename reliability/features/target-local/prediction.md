# Target-local prediction qualification

Evidence: `artifacts/target-local-prediction/20261018T153621123-8e2e2300`.

`TargetLocalPredictionVerify` ran once with seed 20261018 on kernel `5.15.153.1-microsoft-standard-WSL2`, x64, with 4 CPUs.
The container drops all capabilities, has no network, and uses `seccomp=unconfined`.
The acceptance criteria are in [the prediction plan](../../../lode/plans/target-local-prediction.md). The runner hashes that file with the sources.
All 16 source hashes and 3 binary hashes stay unchanged during the run.

Each block runs `profile-zero`, `profile-speedup` at 50%, `original`, and `optimized` in a random order.
The prediction is `1 - Es / E0` from effective durations. The actual reduction is `1 - Toptimized / Toriginal`.

| Configuration | Selected rounds | Peer rounds | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-longer` | 6000 | 3000 | 5 / 5 | 49.77% | 49.50% | 1.12 pp | 1.56 pp | Pass |
| `peer-longer` | 3000 | 6000 | 5 / 5 | -0.52% | 1.01% | 2.82 pp | 4.37 pp | Pass |
| `selected-dominant` | 8000 | 2000 | 5 / 5 | 49.65% | 48.80% | 1.48 pp | 1.76 pp | Pass |

| Configuration | Block | Predicted | Actual | Difference | Overhead ratio | Global delay (ns) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `selected-longer` | 1 | 48.29% | 49.41% | -1.12 pp | 0.9633 | 50,500,133 |
| `selected-longer` | 2 | 50.43% | 50.73% | -0.30 pp | 0.9912 | 50,000,029 |
| `selected-longer` | 3 | 50.16% | 49.50% | +0.66 pp | 1.0054 | 51,500,044 |
| `selected-longer` | 4 | 49.76% | 51.32% | -1.56 pp | 0.9892 | 51,000,050 |
| `selected-longer` | 5 | 49.77% | 48.20% | +1.56 pp | 1.0232 | 51,000,115 |
| `peer-longer` | 1 | -2.38% | -0.60% | -1.78 pp | 0.9904 | 26,500,039 |
| `peer-longer` | 2 | -0.09% | 1.01% | -1.10 pp | 1.0165 | 26,000,047 |
| `peer-longer` | 3 | -3.51% | -0.68% | -2.82 pp | 0.9782 | 26,000,016 |
| `peer-longer` | 4 | -0.52% | 3.34% | -3.86 pp | 0.9662 | 24,500,105 |
| `peer-longer` | 5 | -0.15% | 4.23% | -4.37 pp | 0.9672 | 24,500,033 |
| `selected-dominant` | 1 | 49.42% | 50.03% | -0.61 pp | 0.9959 | 68,000,000 |
| `selected-dominant` | 2 | 50.27% | 48.80% | +1.48 pp | 1.0187 | 68,000,000 |
| `selected-dominant` | 3 | 49.65% | 48.12% | +1.53 pp | 1.0077 | 67,500,000 |
| `selected-dominant` | 4 | 50.81% | 52.57% | -1.76 pp | 1.0034 | 69,500,000 |
| `selected-dominant` | 5 | 48.31% | 48.62% | -0.31 pp | 1.0263 | 70,000,000 |

The pass rule requires 5 valid blocks in each of the three configurations, a median absolute difference of at most 5 percentage points, and every block within 10.
All three configurations pass. Every profile run has complete epochs, zero loss, a valid managed replay, matching global delay and event count, and no ledger failure.
The longest handler interval in any profile run is 1,510,294 ns.

In `selected-dominant`, a 50% optimization leaves the selected thread longer than the peer. Over-credit would show as a prediction above 50%. The median prediction is 49.65%.

The overhead ratio is the `profile-zero` elapsed time divided by the `original` elapsed time.
Its median is 0.9912 in `selected-longer`, 0.9782 in `peer-longer`, and 1.0077 in `selected-dominant`.
Run-to-run variation is of the same size as the overhead, so this run cannot separate them.

## Superseded runs

`artifacts/target-local-prediction/20261018T135308886-ecb06eba` used the first two-configuration criteria and passed them.
Review then found that its profile arm started timing before a shared barrier and its plain arm after a different one.
All five `selected-longer` differences in that run were negative.
`artifacts/target-local-prediction/20261018T145436535-35f0e9cb` added `selected-dominant` and timed both arms from the later thread start. It passed.
Review then required the earlier thread start, so that no thread's work is outside the measurement.
`artifacts/target-local-prediction/20261018T150410931-cfa294a1` used that rule and passed all three configurations.
`artifacts/target-local-prediction/20261018T151318850-8709b5a5` repeated those criteria after `Program.fs` gained the forced-GC mode. It passed.
The current run repeats them again after the forced-GC window counter changed `Program.fs`, a hashed source.
Three exploratory blocks for each of the first two configurations ran before either qualification. They are not part of any result.

## Limits

One host, one kernel, and one qualification run. Five blocks for each configuration cannot establish a general error bound.
The program is two CPU-bound threads with no waits before the join. Its methods allocate nothing, so GC is not exercised.
The `actual` values also vary by several points between blocks with the same work.
Positive differences per configuration: `selected-longer` 2 of 5, `peer-longer` 0 of 5, `selected-dominant` 2 of 5. Five blocks cannot separate a small bias from variation.
The peer pays by spinning, so payment uses CPU. A host with fewer free CPUs than threads can change the result.

See [the tutorial](../../../docsSrc/Tutorials/Target_Local_Prediction.md) and [the support boundary](../../../docsSrc/Explanations/target-local-prediction.md).
