# First cooperative matched block

Retained capture: `artifacts/cooperative-comparison/20261016T023025812-b1a08e73055943a0be64a00b4f68c776`.
Both required reviewers cleared the source before this block.
`LinuxCooperativeComparison` rebuilt the Release workload and ran the portable checks before collection.
The retained native build passed all 14 checks. Retained analysis passed 22 comparison checks and 88 selected replay checks.

The runner executed each declared arm once with 50,000 selected batches.
All four arm processes returned zero. The retained analyzer accepted all four records.
Original inputs remained unchanged. Retained snapshot hashes matched. No runner error occurred.
The runner returned failure because prediction qualification remains unmet.

| Arm | Frozen peer progress | Accepted/source samples | Global-delay delta | Descriptive cost per selected batch |
| --- | --- | --- | --- | --- |
| Physical baseline | 77,700 | Not sampled | 0 ns | 3,191.975–5,364.960 ns |
| Physical optimized | 43,452 | Not sampled | 0 ns | 1,619.575–2,742.570 ns |
| Sampled zero | 88,315 | 456/459 | 0 ns | 9,139.928–9,259.538 ns |
| Sampled half | 95,797 | 461/463 | 66,143,001 ns | 9,231.926–9,360.466 ns |

## Descriptive comparison

| Quantity | Lower | Upper |
| --- | --- | --- |
| Predicted elapsed reduction | −2.4129% | +0.2982% |
| Physical elapsed reduction | +14.0792% | +69.8120% |
| Nominal prediction minus physical change | −72.2249 percentage points | −13.7810 percentage points |

These intervals describe the retained clocks and global-delay correction. They are not accuracy bounds.
The physical optimization remains uncalibrated as a 50% local reduction.
One fixed-order block cannot separate scheduling variation from implementation cost.
The two-percentage-point accuracy gate remains unmet. `passed` and `predictionAccuracyEstablished` remain false.

## Frozen ownership

The treatment freezes peer command and generation 275 before cleanup.
Its frozen peer local credit is 65,643,054 ns, against global delay of 66,143,001 ns.
The pending request has no accepted child at that boundary.
Cleanup later raises peer local credit to 66,143,193 ns without changing frozen global delay.
The frozen pending ownership and residual remain qualification limits after cleanup.
Both sampled arms also retain source tails, unresolved boundary membership, and unresolved instrumentation attribution.

## Cost observations

The physical start brackets span 108,647,741 ns and 56,148,575 ns.
The sampled start brackets span 5,972,150 ns and 5,712,517 ns.
The sampled-half end bracket spans 714,414 ns. The analyzer retains all these widths.

The zero arm has no selected pause or payment.
The treatment has 143 selected pauses, 143 selected payments, and 275 peer payments.
Its recorded selected pause intervals sum to 35,568,894 ns.
Its selected payment intervals sum to 20,003,090 ns, while accepted native selected wait totals 143,001 ns.
It records 79,729 direct receipt-readiness calls. The zero arm records none.

These observations identify costs to inspect. They do not establish the cause of the descriptive prediction difference.
The next investigation must separate pause, payment, and receipt-observation costs before changing the Coz accounting rule.
Earlier timing captures used different shared work and cannot serve as unchanged baselines.

See [the comparison reference](../../../docsSrc/Reference/cooperative-comparison.md),
[the physical execution record](cooperative-physical.md), and [the lifecycle plan](../../../lode/plans/experiment-lifecycle.md).
