# Nominal-event credit evidence

Diagnostic capture: `artifacts/nominal-event-credit/20261016T084115566-2542f0ad`.

The diagnostic uses 50,000 selected batches, probe interval 8192, owner draining, direct receipt readiness, finite measurement, receipt preparation, and owner/controller execution.
All records declare `sampleCreditPolicy="nominal-event-v1"` and `nominalSamplePeriodNanoseconds=1000000`.

## Diagnostic outcomes

All three processes exit zero. All three expected outcomes pass full replay.
Original source and binary inputs remain unchanged.

| Case | Completed | Accepted credit | Selected payments | Outcome |
| --- | --- | ---: | ---: | --- |
| Zero speedup | Yes | 0 ns | 0 | Passed |
| 50% speedup | Yes | 99,000,000 ns | 24 | Passed |
| Invalidate after selected payment | No | 500,000 ns | 1 | Expected CLR lifetime rejection |

The treatment freezes 243 accepted samples from 245 source records.
Its frozen global delay is 99,096,482 ns.
The final cleanup state records 99,096,644 ns of peer payment and 96,482 ns of selected payment.
Cleanup values do not replace frozen measurement inputs.

The zero and treatment measurements remain unqualified.
The treatment retains native ownership and ledger residual limits at the frozen boundary.
Both retain source-tail, boundary-membership, and instrumentation-attribution limits.
The invalidation case retains an invalid measurement even though expected rejection passes the diagnostic.

## Physical comparison

Capture: `artifacts/nominal-physical-comparison/20261016T084324804-2036c15798fd4d1699ee0a0b54520262`.

The four-arm plan binds `sampleCreditPolicy="nominal-event-v1"` with `sampleCreditEvidenceVersion=1`.
It uses the same batch count, probe interval, receipt preparation, and owner/controller settings as the diagnostic.
All four processes exit zero. All arms complete with zero invalid arms and no runner or integrity errors.
The runner exits 1 because prediction remains unqualified.

| Arm | Lower effective ns/progress | Upper effective ns/progress |
| --- | ---: | ---: |
| Physical baseline | 3491.82266 | 3492.38242 |
| Physical optimized | 1608.69224 | 1608.94676 |
| Sampled zero | 4364.96016 | 4365.62532 |
| Sampled half | 4404.99688 | 4420.80444 |

The descriptive sampled effective elapsed reduction is **-1.2794% to -0.9019%**.
Effective elapsed time subtracts frozen global delay from raw elapsed time.
The physical elapsed reduction is **53.9224% to 53.9371%**.
The sampled shortfall is **54.8243 to 55.2165 percentage points**.
These endpoint ranges are not confidence intervals or accuracy bounds.

The treatment credits 243 mapped events at 500,000 ns each, for 121,500,000 ns.
It freezes 333 accepted samples from 335 source records and a global delay of 121,933,152 ns.
Its raw elapsed range is 342,182,996–342,973,374 ns.
Subtracting frozen global delay gives 220,249,844–221,040,222 ns of effective elapsed time.
The zero arm records 218,248,008–218,281,266 ns.

The treatment records 50 selected pauses and 297 admitted selected checks.
The zero arm records no selected pauses and 209 admitted checks.
The treatment source has 243 mapped and 92 unmapped events. The zero source has 181 mapped and 38 unmapped events.
Both sources report zero loss and throttling.
These differences do not isolate observer cost, scheduling, or attribution error.
Separate earlier interval captures cannot establish the causal effect of changing policy.

Nominal-event credit matches the inspected upstream event rule but does not resolve prediction accuracy in this block.
It remains opt-in. The next accuracy investigation must separate observer cost and sample attribution under a controlled comparison.
Finite membership, native ownership, local optimization calibration, and the single-block limit also remain unresolved.

## Deterministic checks

The library suite passes 94 tests before the final assertion refinement.
The five nominal tests pass after the refinement, including fractional carry through non-runnable and other-location events.
Managed cadence checks pass. Sampled replay passes 32 checks, selected replay passes 109, and comparison analysis passes 35.
Both PowerShell outcome and comparison-runner checks pass.
The earlier prepared owner/controller comparison replays unchanged except for explicit default interval-policy metadata.

See [the estimator explanation](../../../docsSrc/Explanations/nominal-event-credit.md), [reference](../../../docsSrc/Reference/nominal-event-credit.md), and [capture procedure](../../../docsSrc/How_Tos/run-nominal-event-credit.md).
