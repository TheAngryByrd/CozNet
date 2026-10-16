# Offline epoch prefix replay

The selected-payment analyzer now replays completed source epoch prefixes.
It preserves workload failure and prediction qualification independently of prefix consistency.
The producer adds `sourceEpochPrefixEvidenceVersion = 1` for selected-payment records.

## Contract checks

These commands pass with 103 selected-payment checks and 22 comparison checks:

```powershell
python native/BoundSelectedPaymentAnalysisChecks.py
python native/CooperativeComparisonChecks.py
```

The workload Release build also passes.
The new marker is an additive serialization field. This change does not alter sampling, credit, or payment.

| Check | Required behavior |
| --- | --- |
| `test_replay_preserves_exact_prefix_arithmetic_and_claim_limits` | Recompute counts and deltas without mutating inputs or claiming selected CPU. |
| `test_legacy_absence_unversioned_presence_and_failed_collection_are_distinct` | Separate absent, unversioned, and unavailable evidence. |
| `test_later_workload_failure_keeps_valid_source_evidence` | Preserve verified source evidence without passing a failed workload. |
| `test_version_requires_fields_and_rejects_unknown_or_boolean_versions` | Enforce the declared version and required field. |
| `test_failed_measurement_capture_does_not_claim_frozen_membership` | Failed capture retains accepted membership without claiming a frozen endpoint. |
| `test_empty_epoch_and_zero_prefix_remain_distinct` | Preserve null versus zero and distinguish enabled-counter totals from nominal totals. |
| `test_unmapped_first_sample_does_not_use_a_later_mapped_sample` | Keep the original first sample and exclude it from mapped totals. |
| `test_baseline_source_clock_and_counter_corruption_reject` | Reject foreign, enabled, failed, mistimed, or inconsistent baselines. |
| `test_missing_extra_reordered_and_nonarray_epochs_reject` | Require a complete ordered epoch list. |
| `test_first_sample_identity_index_numeric_types_and_delta_reject` | Reject changed first records, Boolean counts, and wrong subtraction. |
| `test_failed_workload_cannot_hide_source_loss_or_missing_cleanup` | Require valid source closure even after a later workload failure. |
| `test_frozen_prefix_excludes_a_later_first_sample` | Retain a source-tail first sample without including it in accepted mapped totals. |

## Replay of retained Linux evidence

Inputs come from `artifacts/epoch-prefix-payment/20261016T050549443-224121bb`.
The new report is `artifacts/epoch-prefix-replay/20261016T051938128/analysis-final.json`.
The directory retains input hashes and analyzer identity separately from the original capture.
Replay preserves the original inputs and analysis. No new timing experiment was run.

| Case | Prefix status | Source epochs | Source samples | Accepted samples | Accepted mapped baselines | Enabled prefix total, ns | Nominal total, ns |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Zero | verified | 1 | 458 | 456 | 0 | 0 | 0 |
| 50 percent | verified | 130 | 417 | 415 | 61 | 59999000 | 61000000 |
| Expected invalidation | unavailable | — | — | — | — | — | — |

All three cases retain their expected successful verification outcome.
The invalidation case remains incomplete and has no validated prefixes.
The retained records predate the version marker, so `declaredVersion` is null while the replay contract version is 1.
Versioned input handling is covered by synthetic full-record checks. The new producer marker has build validation, without a fresh runtime capture.

## Limits and failure behavior

A corrupt prefix rejects the complete case replay. The matched comparison marks that arm invalid and retains the other arms.
Legacy compatibility permits removal of both evidence fields to produce `absent`.
Consumers that need prefix evidence must require `epochPrefixes.status == "verified"`.
Overall `evidenceVerified` does not establish prefix availability.

The replay verifies internal consistency, not independent authenticity of a captured counter read.
It cannot recover missing prefixes from older artifacts.
It repeats counter and batch checks when needed to verify prefixes in failed workloads.
That repetition adds offline processing cost and does not change runtime accounting.
Enabled CPU prefixes do not prove selected-method CPU or sampling accuracy.
Prediction remains unqualified.

See [the prefix reference](../../../docsSrc/Reference/bound-cpu-session.md)
and [the capture evidence](epoch-prefix-evidence.md).
