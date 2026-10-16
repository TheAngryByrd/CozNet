# Eligibility epoch credit sensitivity

The retained snapshot-cache comparison cannot attribute its prediction gap to epoch-baseline exclusion alone through credit arithmetic.
This analysis changes no source code, accounting rule, or runtime measurement.

## Inputs and replay

The input directory is `artifacts/sample-batch-cache/20261016T044531867/20261016T044539201-e5eeebcff8a943f3aa458744ac8b68b1`.
The input SHA-256 values are:

| Input | SHA-256 |
| --- | --- |
| `sampled-zero.json` | `9623018947c3c2dbc328fa6d021bdff76948d2883a3adbadd63e478d50bc0cfa` |
| `sampled-half.json` | `260f0f840159f377ff3681dc0a54decdfabda4e5ee0d541cd87d6a3ac86efc8f` |

The analysis checks every accepted index, nondecreasing epoch, recorded credit, total credit, and frozen endpoint credit.
Both frozen windows start with zero accepted samples and end at the full accepted prefix.
Each accepted sample has a nominal period of 1,000,000 ns.
The first accepted record of each epoch establishes its baseline.
Subsequent mapped records earn nominal-period credit at the declared percentage, with integer remainder retention.

| Accepted class | Zero-speedup count | Treatment count |
| --- | ---: | ---: |
| Unmapped baseline | 1 | 77 |
| Unmapped subsequent sample | 295 | 176 |
| Mapped baseline | 0 | 61 |
| Mapped subsequent sample | 198 | 124 |
| Total | 494 | 438 |

Replay reproduces zero credit for the reference and 62,000,000 ns for the treatment.
These counts describe accepted records. They do not classify CPU between observations.

## Fixed-trace calculation

The calculation assigns full nominal credit to all 61 mapped treatment baselines.
This adds `61 * 1,000,000 * 50 / 100 = 30,500,000` ns of hypothetical credit.
It holds timestamps, progress, and the global-delay-minus-credit residual fixed.
It therefore subtracts that amount from the treatment's existing corrected duration.

| Duration | Lower, ns | Upper, ns |
| --- | ---: | ---: |
| Zero-speedup corrected duration | 504320289 | 504407836 |
| Original treatment corrected duration | 487374140 | 487637637 |
| Hypothetical treatment corrected duration | 456874140 | 457137637 |

All arms complete 50,000 progress units, so the common progress denominator cancels in the reduction ratio.
The lower reduction is `1 - 457137637 / 504320289`.
The upper reduction is `1 - 456874140 / 504407836`.
The result is **9.3557%–9.4237%**, compared with the recorded physical reduction of **50.4595%–50.4720%**.

This calculation is not a prediction, CPU bound, or confidence interval.
It is an arithmetic sensitivity under fixed observations.
Changing credit online can change debt, payments, scheduling, samples, and elapsed time.
Those effects require a separate declared experiment.
Counter increments can also differ from nominal periods. This calculation does not bound an alternative counter-weighted estimator.

## Consequence

Baseline exclusion loses nominal selected credit in this trace, but restoring all that nominal credit does not close its arithmetic gap.
The result does not justify relaxing the exclusion rule.
Any partial-credit design needs retained disabled counter baselines and explicit sample ownership across each resume.
Enabled CPU and selected-method CPU remain different quantities.
Observer cost and attribution remain required accuracy work.

See [snapshot-cache evidence](sample-batch-snapshots.md)
and [the counter explanation](../../../docsSrc/Explanations/cpu-counter-attribution.md).
