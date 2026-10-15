# Calibration variation and overhead sensitivity

Physical calibration measures the cost of a candidate optimization before comparison with a virtual speedup.
The first contiguous series had mean reduction `0.5127666452` and interval half-width `0.0345791551`.
Its distance-plus-half-width from 50 percent exceeded the entire two-percentage-point prediction-error budget.
Temporal drift is one hypothesis for the variation. That series does not establish its cause.

Interleaved calibration places short baseline, optimized, and control segments near each other in randomized rounds.
The block remains the statistical unit because segments within a block share execution conditions.
A smaller observed interval would not identify the cause of the difference from the contiguous series.
The Student-t interval depends on its statistical assumptions and does not bound systematic error.

The control measures a constant-return method through the same loop and clock path.
The sensitivity model assumes each workload's external cost lies independently between zero and the block's control total.
That assumption permits different external costs for baseline and optimized work.
The resulting endpoints show how the reduction changes under that assumption.
The constant-return method does not prove that it bounds either workload's external cost.

The reported sensitivity is therefore not a verified overhead bound or clock bound.
It does not justify automatic virtual-speedup selection or establish prediction accuracy.
Clock bias, method attribution, runtime effects, and matched physical prediction still require separate evidence.

See [the formulas](../Reference/interleaved-calibration.md), [the procedure](../How_Tos/run-interleaved-calibration.md), and [the contiguous evidence](../../reliability/features/native-signal/physical-calibration.md).
