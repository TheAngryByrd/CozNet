# Interleaved calibration evidence

Run `20261015T012028818-3c78e708` completed one warmup and eight measured blocks on Linux x64.
Each block contained 100 randomized rounds with baseline, optimized, and control segments.
Each segment completed 500 verified checksum calls. The saved seed was `20261015`.
The runner captured source and binary snapshots before execution and reported unchanged inputs afterward.
Host checks, Linux checks, physical baseline, physical optimized, and calibration processes all returned zero.

| Statistic | Value |
| --- | ---: |
| Mean block CPU reduction | 0.4999657786732118 |
| Sample standard deviation | 0.01049266809576704 |
| Student-t interval half-width | 0.008772090049578766 |
| Maximum observed overhead shift | 0.00062573706114466 |
| Distance from 0.5 plus interval and sensitivity | 0.009432048437511635 |

The combined statistic is 0.943 percentage points, below the two-point prediction-error budget.
It is not a verified total error bound. Clock bias and the assumed overhead range remain unverified.
The interval is smaller than the retained contiguous interval. This comparison does not establish temporal drift as the cause.
No repeated series replaced this result. No prediction-accuracy claim follows from it.

`artifacts/interleaved-calibration/analyze.py` independently checked all 2,700 segment records and their block journals.
It checked declared work and order, cumulative clocks, interval containment, 111 source hashes, nine binary hashes, and all reported statistics.
The source snapshot omits shared imports and referenced project sources. It is not a complete rebuild checkout.
The evidence remains under `artifacts/interleaved-calibration/20261015T012028818-3c78e708/`.

## Portable checks

The seven registered checks passed on Windows x64 and Linux x64 through the workload verifier.
The focused Windows commands also returned zero. The verifier emits no case-count summary.
The live snapshot predates the assertion that each block exercises all six mode orders.
That assertion passed afterward in focused Windows and Linux checks. The measurement and analysis code did not change.

| Check | Contract |
| --- | --- |
| `PlanPreservesDeclaredWorkAndOrder` | Each round contains all three modes in reproducible order. Each block exercises all six mode orders. |
| `SegmentSizeAndClockConsistency` | Work count and enclosing clocks define a complete segment without intermediate overflow. |
| `BlockStatisticsAndSensitivity` | Eight block totals produce the independently specified statistics. |
| `InvalidSequencesCannotProduceEstimate` | Missing, failed, reordered, duplicated, or inconsistent records suppress estimates. |
| `DominatingControlAndNegativeReduction` | Invalid control totals fail and negative reductions remain negative. |
| `CaptureContinuesAfterMeasurementFailure` | Segment failure preserves later measurements. |
| `DriverPreservesBlocksAndFailureOutcomes` | Planning precedes setup, journals precede later blocks, and checkpoint failure suppresses estimates. |

See [the reference](../../../docsSrc/Reference/interleaved-calibration.md), [interpretation](../../../docsSrc/Explanations/calibration-sensitivity.md), and [contiguous evidence](physical-calibration.md).
