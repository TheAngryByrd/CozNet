# Bound sample membership

`native/AnalyzeBoundSampleMembership.py` classifies bound source timestamps against frozen measurement brackets.
Its policy is `linux-monotonic-bound-membership-v1`.
It does not change recorded credit or measurement qualification.

## Input contract

The input is one finite selected-payment case with enabled [bound sample clock evidence](cpu-sample-clock.md#bound-sampled-evidence).
The parser first runs full selected-payment replay.
It requires consistent clock readings, frozen endpoints, a closed source, valid counter transitions, and a complete batch without recorded loss.
These source checks also apply to failed workloads.

The policy accepts `linux-perf`, `CLOCK_MONOTONIC`, clock ID `1`, and attribute size `96`.
It accepts `.NET 10.0.0` on X64 with one billion managed ticks per second.
Other clock paths require a separate policy review.
Metadata records declarations. It does not attest the running binaries.

Source timestamps must fit signed 64-bit integers and must not exceed the final source observation.
The existing replay verifies source order, owner identity, native range labels, epochs, and recorded credit.
The [membership rules](sample-membership.md#membership-rules) retain `Before`, `Inside`, `After`, and `BoundaryAmbiguous` classifications.
The interval is half-open. Uncertain endpoint brackets remain explicit.

## Output fields

| Field | Meaning |
| --- | --- |
| `comparisonDomainAccepted`, `reasons` | Policy acceptance and rejection details |
| `observations` | Source index, timestamp, eligibility epoch, nominal weight, selected status, frozen-prefix acceptance, credit, and source membership |
| `membershipCounts` | Source record counts for each classification |
| `membershipTotals` | Lower and upper counts, nominal weights, and recorded credit inside the observed window |
| `sampleAccountingGroups` | Totals grouped by source membership, frozen-prefix acceptance, and selected status |
| `recordedCreditBySourceMembership` | Partition of all replayed credit |
| `replayedCreditNanoseconds` | Unchanged total recorded credit |
| `workloadCompleted`, `workloadPassed` | Original workload outcomes |
| `workloadFailure`, `verificationFailure`, `cleanupErrors` | Original failure evidence |
| `clockEvidence`, `measurementEvidence` | Original clock and measurement records |
| `rawSourceSamples`, `rawAcceptedSamples`, `rawSourceEpochSamples` | Original sample records |
| `inputSha256`, `analyzerSources` | Input hash and hashes of loaded local analysis modules |

Analysis files must remain unchanged during execution.
Module hashes describe files on disk after replay. They do not attest loaded Python code.

Inside observations contribute to both limits. Ambiguous observations contribute only to the upper limit.
`unacceptedSelectedNominalNanoseconds` describes retained selected observations outside the frozen acceptance prefix.
It does not estimate missing credit. Samples absent from the capture remain unknown.

The bound workload does not record per-sample receipt timestamps.
The report therefore uses frozen-prefix acceptance and does not invent receipt-time membership.
Accepted samples outside the source-time window keep their recorded credit.
Unaccepted samples inside the source-time window receive no retrospective credit.

## Failure and accuracy limits

Exit code zero means that the comparison domain passed this policy. It does not mean the workload succeeded.
Rejected reports contain no classified observations and have null membership totals.
The output path must not exist. The command does not replace input or output files.
Malformed JSON, duplicate fields, and non-finite numbers produce rejection reports when the output path is writable.
Arithmetic failures during replay also produce rejection reports.

The policy assumes that no unobserved fast-timekeeper anomaly changes membership.
Clock consistency does not establish physical-clock accuracy, exact event ordering, exact CPU attribution, or prediction accuracy.
All corresponding qualification fields remain false.

See [the procedure](../How_Tos/analyze-bound-sample-membership.md) and [upstream boundary semantics](../Research/sample-boundary-semantics.md).
