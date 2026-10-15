# Observed sample membership

`native/AnalyzeSampleMembership.py` classifies recorded source timestamps against frozen measurement endpoints.
The policy is `linux-monotonic-observed-membership-v1`.
It requires valid CPU-counter evidence before parsing the clock evidence.

```powershell
python native/AnalyzeSampleMembership.py capture.json membership.json
```

The output path must not exist. Exit code zero means that the comparison domain passed this policy.
Workload success remains a separate result. Failed workloads retain their failures and native closure state.
The report includes SHA-256 hashes for the input, analyzer, and imported counter parser.
Non-finite JSON numbers, including overflowing exponents, produce a complete rejection report.

## Accepted clock configuration

The policy accepts the current `linux-perf` backend with `CLOCK_MONOTONIC`, clock ID 1, and attribute size 96.
It accepts `.NET 10.0.0` on X64 with one billion managed ticks per second.
The source must declare monotonic nanoseconds. Other runtime versions, architectures, domains, and units require a separate policy review.

Both clock readings must have zero namespace offsets and the same nonempty context identity.
Native readings must lie inside their managed brackets. Those brackets must enclose the measurement endpoints.
The parser checks raw values as well as the producer's consistency result.
Metadata records declarations. It does not attest the running kernel or runtime binary.

The shared counter parser rejects loss, changed sample targets, regressing source timestamps, and regressing receipt timestamps.
This policy also rejects source timestamps after their recorded receipt and timestamps outside the signed 64-bit range.
It does not sort or discard contradictory observations.

## Membership rules

Let `[S0,S1]` be the start bracket and `[E0,E1]` be the end bracket.
The parser requires `S0 <= S1 <= E0 <= E1`.

| Source timestamp `T` | Membership |
| --- | --- |
| `T < S0` | `Before` |
| `S1 <= T < E0` | `Inside` |
| `E1 <= T` | `After` |
| All other values | `BoundaryAmbiguous` |

The interval is half-open. A zero-width start includes equality. A zero-width end excludes equality.
Receipt after the window does not exclude a source timestamp inside the window.
Producer acceptance and observed membership remain separate fields.

## Output

`comparisonDomainAccepted` reports policy acceptance. `reasons` retains rejection details.
`observations` retains record indexes, source and receipt timestamps, nominal periods, selected status, producer acceptance, and membership.
`membershipCounts` counts each classification.
`membershipTotals` provides lower and upper counts and nominal weights for all samples and selected samples.
Inside samples contribute to both limits. Ambiguous samples contribute only to the upper limit.
Before and after samples contribute to neither limit.
These weights do not measure exact CPU intervals or verify selected instruction attribution.

`clockEvidence`, `measurementEvidence`, and `rawSamples` preserve available input evidence, including rejected observations.
Rejected reports have null membership totals and no classified observations.
`assumption` states that no unobserved fast-timekeeper anomaly changes membership.
`physicalClockAccuracyEstablished`, `exactEventOrderingEstablished`, and `predictionAccuracyEstablished` remain false.
## Recorded credit membership

`--sample-credits` also requires [nominal credit reconciliation](cpu-counter-evidence.md).
Exit code zero then requires both clock membership and credit reconciliation.
Invalid credit evidence rejects the optional report even when clock evidence alone passes.
The default mode does not require credit fields.

`creditMembership.sampleAccountingGroups` groups records by source membership, receipt membership, acceptance, and selected status.
Each group retains its sample count, nominal periods, and recorded credit.
A source-inside sample rejected during measurement differs from one received after the endpoint.
Neither gains credit through this analysis.

`recordedCreditBySourceMembership` partitions all replayed credit across the four source classifications.
`recordedCreditInObservedWindow` includes inside credit in both limits and ambiguous credit only in the upper limit.
`replayedCreditNanoseconds` retains the reconciled total.
These ranges describe credit already recorded. They do not bound hypothetical missing credit or paid native delay.
Eligibility-transition verification, unreceived-credit bounds, and prediction accuracy remain false.

See [clock alignment](../Explanations/sample-clock-alignment.md), [CPU clock evidence](cpu-sample-clock.md), and [counter evidence](cpu-counter-evidence.md).
