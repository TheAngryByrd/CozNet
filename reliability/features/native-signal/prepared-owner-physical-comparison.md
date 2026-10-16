# Prepared owner/controller physical comparison

Capture: `artifacts/prepared-owner-physical-comparison/20261016T080227394-3795ae0da1b04465bb7fba3d10586e3b`.

The runner uses 50,000 selected batches, probe interval 8192, receipt preparation, and owner/controller execution.
The four-arm plan binds ownership with `ownerController=true` and `controllerOwnershipEvidenceVersion=1`.
Both sampled results report matching ownership and preparation metadata.
Physical arms run without profiler activation. Service, dispatch, and pause timing are disabled in sampled arms.

## Outcome

All four processes exit zero. All four arms complete, with zero invalid arms.
Runner and integrity error lists are empty. Original inputs remain unchanged.
The runner exits 1 because prediction remains unqualified.
The portable comparison suite passes 32 checks. Controlled runner checks pass for both ownership modes and existing positional calls.

| Arm | Lower effective ns/progress | Upper effective ns/progress |
| --- | ---: | ---: |
| Physical baseline | 3378.47756 | 3378.73558 |
| Physical optimized | 1604.86156 | 1605.12132 |
| Sampled zero | 4296.36830 | 4297.03926 |
| Sampled half | 3277.35694 | 3281.22902 |

Sampled effective elapsed reduction is **23.6278–23.7299%**.
Physical elapsed reduction is **52.4898–52.5011%**.
The descriptive prediction falls short by **28.7599–28.8733 percentage points**.
Effective elapsed time subtracts the frozen global-delay delta from recorded elapsed time.
These intervals describe boundary uncertainty. They are not statistical confidence intervals or qualified accuracy bounds.

## Frozen treatment state

Frozen global delay is 82,505,872 ns. Accepted nominal credit is 82,500,000 ns.
Neither selected nor peer ownership has a pending native request at the end.
Selected local delay is 82,505,807 ns, leaving a 65 ns ledger residual.
Peer local delay equals global delay.
The frozen prefix contains 241 accepted sample records. The retained source contains 243 records.

## Decision

The [prepared service comparison](prepared-service-ownership.md) supports lower receipt and submission cost under owner/controller execution.
This physical comparison still fails to establish prediction accuracy.
Separate earlier blocks cannot isolate the mode's effect on prediction error.
Keep owner/controller execution opt-in.
Investigate sampled attribution and boundary membership before claiming that the remaining error is removable controller cost.
Local optimization calibration, native ownership qualification, and the single-block limit also remain unresolved.

See [the runner reference](../../../docsSrc/Reference/cooperative-comparison.md) and [capture procedure](../../../docsSrc/How_Tos/capture-cooperative-comparison.md).
