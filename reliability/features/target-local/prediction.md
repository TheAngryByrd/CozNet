# Target-local prediction qualification

Evidence: `artifacts/target-local-prediction/20261018T150410931-cfa294a1`.

`TargetLocalPredictionVerify` ran once with seed 20261018 on kernel `5.15.153.1-microsoft-standard-WSL2`, x64, with 4 CPUs.
The container drops all capabilities, has no network, and uses `seccomp=unconfined`.
The acceptance criteria are in [the prediction plan](../../../lode/plans/target-local-prediction.md). The runner hashes that file with the sources.
All 16 source hashes and 3 binary hashes stay unchanged during the run.

Each block runs `profile-zero`, `profile-speedup` at 50%, `original`, and `optimized` in a random order.
The prediction is `1 - Es / E0` from effective durations. The actual reduction is `1 - Toptimized / Toriginal`.

| Configuration | Selected rounds | Peer rounds | Valid blocks | Median predicted | Median actual | Median absolute difference | Max absolute difference | Result |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `selected-longer` | 6000 | 3000 | 5 / 5 | 49.36% | 50.57% | 1.27 pp | 4.80 pp | Pass |
| `peer-longer` | 3000 | 6000 | 5 / 5 | 1.57% | -2.12% | 2.86 pp | 3.95 pp | Pass |
| `selected-dominant` | 8000 | 2000 | 5 / 5 | 49.18% | 50.48% | 1.32 pp | 3.58 pp | Pass |

| Configuration | Block | Predicted | Actual | Difference | Overhead ratio | Global delay (ns) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `selected-longer` | 1 | 49.36% | 50.57% | -1.21 pp | 1.0136 | 52,000,036 |
| `selected-longer` | 2 | 49.64% | 48.37% | +1.27 pp | 1.0117 | 51,000,000 |
| `selected-longer` | 3 | 46.94% | 49.71% | -2.77 pp | 1.0043 | 52,500,034 |
| `selected-longer` | 4 | 48.60% | 53.40% | -4.80 pp | 0.9472 | 51,500,000 |
| `selected-longer` | 5 | 51.41% | 51.53% | -0.12 pp | 1.0272 | 50,500,039 |
| `peer-longer` | 1 | 0.13% | 2.29% | -2.17 pp | 0.9929 | 25,500,031 |
| `peer-longer` | 2 | 1.57% | -2.12% | +3.69 pp | 1.0317 | 26,000,139 |
| `peer-longer` | 3 | -2.53% | -3.35% | +0.82 pp | 0.9858 | 25,500,027 |
| `peer-longer` | 4 | 1.75% | -2.19% | +3.95 pp | 1.0232 | 24,500,115 |
| `peer-longer` | 5 | 3.96% | 1.10% | +2.86 pp | 1.0348 | 25,500,121 |
| `selected-dominant` | 1 | 51.80% | 50.48% | +1.32 pp | 1.0141 | 66,000,000 |
| `selected-dominant` | 2 | 51.13% | 50.78% | +0.34 pp | 1.0381 | 69,000,000 |
| `selected-dominant` | 3 | 48.73% | 49.89% | -1.15 pp | 1.0010 | 68,000,000 |
| `selected-dominant` | 4 | 49.18% | 52.76% | -3.58 pp | 0.9567 | 68,500,000 |
| `selected-dominant` | 5 | 48.16% | 50.02% | -1.85 pp | 0.9908 | 70,500,000 |

The pass rule requires 5 valid blocks in each of the three configurations, a median absolute difference of at most 5 percentage points, and every block within 10.
All three configurations pass. Every profile run has complete epochs, zero loss, a valid managed replay, matching global delay and event count, and no ledger failure.
The longest handler interval in any profile run is 1,519,779 ns.

In `selected-dominant`, a 50% optimization leaves the selected thread longer than the peer. Over-credit would show as a prediction above 50%. The median prediction is 49.18%.

The overhead ratio is the `profile-zero` elapsed time divided by the `original` elapsed time.
Its median is 1.0117 in `selected-longer`, 1.0232 in `peer-longer`, and 1.0010 in `selected-dominant`.
Run-to-run variation is of the same size as the overhead, so this run cannot separate them.

## Superseded runs

`artifacts/target-local-prediction/20261018T135308886-ecb06eba` used the first two-configuration criteria and passed them.
Review then found that its profile arm started timing before a shared barrier and its plain arm after a different one.
All five `selected-longer` differences in that run were negative.
`artifacts/target-local-prediction/20261018T145436535-35f0e9cb` added `selected-dominant` and timed both arms from the later thread start. It passed.
Review then required the earlier thread start, so that no thread's work is outside the measurement. The current run uses that rule.
Three exploratory blocks for each of the first two configurations ran before either qualification. They are not part of any result.

## Limits

One host, one kernel, and one qualification run. Five blocks for each configuration cannot establish a general error bound.
The program is two CPU-bound threads with no waits before the join. Its methods allocate nothing, so GC is not exercised.
The `actual` values also vary by several points between blocks with the same work.
In `peer-longer`, four of five differences are positive. Five blocks cannot show whether that is bias.
The peer pays by spinning, so payment uses CPU. A host with fewer free CPUs than threads can change the result.

See [the tutorial](../../../docsSrc/Tutorials/Target_Local_Prediction.md) and [the support boundary](../../../docsSrc/Explanations/target-local-prediction.md).
