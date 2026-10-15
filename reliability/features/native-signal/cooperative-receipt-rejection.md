# Rejected managed receipt intervals

## Contract

The bound sampled workload retains the exact arguments rejected by `CooperativeReceiptInterval.Parse`.
`receiptIntervalRejection` contains the raw timestamps, frequency, and request identity.
`receiptIntervalCaptureVersion=1` identifies this evidence contract.
The capture preserves the original parser exception and prevents later recovery through the same capture object.
The workload does not assign the returned accounting state or append payment after rejection.
Earlier request watchdog failures do not produce this parser-rejection record.

The diagnostic does not change the 100 ms bound, native waits, or CPU credit rules.
It cannot reconstruct the missing endpoint in an older capture.
See [the earlier one-CPU receipt failure](owner-drain-single-cpu.md#owner-trial-02-a-missing-rejected-receipt-endpoint).

## Controlled evidence

| Requirement | Check |
| --- | --- |
| Preserve request, raw clocks, serialization, and original exception on both slots | `RejectedIntervalsRetainIdentityAndOriginalFailure` |
| Accept the exact 100 ms boundary without rejection evidence | `AcceptedIntervalsDoNotCreateFailureEvidence` |
| Bind rejection to either request slot | `test_rejected_receipts_verify_both_request_slots_without_acceptance` |
| Reject missing evidence, altered identity, or altered clock arguments | `test_missing_rejection_and_changed_request_are_rejected` |
| Reject a valid interval labeled as a failure | `test_valid_interval_cannot_be_reported_as_rejected` |
| Prevent rejected payment acceptance | `test_rejected_request_cannot_enter_accepted_payments` |
| Prevent a completed result with rejection evidence | `test_completed_run_cannot_carry_a_rejection` |
| Preserve the legacy workload path | `test_new_receipt_capture_is_checked_in_legacy_workload` |
| Match receipt and failure-capture clock frequencies | `test_rejected_receipt_frequency_matches_failure_capture` |
| Place rejection before the first failure capture | `test_rejected_receipt_precedes_first_failure_capture` |

The managed checks run through `--verify-cooperative-managed` on Windows and Linux.
The replay suites contain 20 peer checks and 51 selected-payment checks.
The outcome-reader fixture rejects missing, malformed, and unsupported capture versions when version 1 is requested.
Legacy replay records without a version remain readable.

## Accepted coverage limits

Controlled checks exercise the capture helper, not rejection through either workload call site.
They cannot detect removal of a call site or assignment of returned ledger state before parsing.
Source review verifies both call sites and their assignment order. This is a coverage limit, not runtime proof.
A future controlled workload seam must cover both paths before claiming that proof.

`ReadAndAccept` can update receipt tracking and diagnostic observation before interval rejection.
The workload commits neither the returned ledger state nor accepted payment after rejection.
Replay binds the original exception message exactly. A message change requires a matching replay update or verification fails.
Earlier watchdog failures remain outside this parser-specific capture contract.

Standalone replay permits legacy records without both new fields.
The current runner requires version 1 and rejects that downgrade.
Legacy compatibility and the narrow capture scope are intentional. They do not establish live rejection coverage.

## Live evidence

Unrestricted capture: `artifacts/receipt-interval-capture/20261015T213722880-e5ecf297`.
The owner collector uses qualified-sample cadence, 50,000 requested batches, child CPU capture, and pause timing.
All three cases pass. Zero speedup and treatment each complete 50,000 batches.
All records report capture version 1 and a null rejection.
The runner verifies unchanged source and binary hashes.

Constrained capture: `artifacts/receipt-interval-single/20261015T213835463/01-owner`.
The declared single trial uses the same binaries and guest CPU 0.
The zero-speedup case completes 50,000 batches.
Treatment fails after 24,244 batches at 2,000,093 ns of peer debt.
The expected-invalidation case fails after 295 batches at 2,499,909 ns of peer debt before selected payment.
It does not reach the intended invalidation.
Replay verifies all three records and preserves both failures.
No record contains a parser rejection. No unchanged case was retried.

These live captures exercise successful receipt parsing and other failure paths.
They do not establish that a live receipt rejection reaches the new diagnostic path.
Controlled checks establish retention and replay of rejected arguments.
Local artifacts are not distributed with this document.

See [the reference](../../../docsSrc/Reference/cooperative-managed.md#rejected-managed-receipt-intervals).
