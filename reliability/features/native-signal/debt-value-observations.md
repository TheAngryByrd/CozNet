# Debt observation value records

`BoundSampledPaymentWorkload.ObserveDebt` previously created one reference record for every observation.
The record contains nine signed 64-bit fields.
It now uses a readonly value record, including nullable copies for peaks, the final observation, and failure capture.
Every observation still reads the timestamp, observes accounting, and applies the unchanged debt gate.

## Allocation regression

The same new check ran first with the reference representation.
It failed with this message:

```text
Debt construction and nullable retention must preserve every value without allocation; observed 880000 bytes.
```

The retained log is `artifacts/debt-observation-reference-check.log`.
The check warms 1,000 calls, then measures 10,000 calls on the current thread.
A non-inlined helper stores each observation and consumes every field in a checked result.
The static retained observation resets in `finally`, including after failure.

After the representation change, the same check passes with zero allocated bytes.
The Release build and `--verify-cooperative-managed` exit zero.
Their logs are `artifacts/debt-observation-value-build.log` and `artifacts/debt-observation-value-check.log`.

| Check | Required behavior |
| --- | --- |
| `DebtRecordsAllocateNoCallingThreadMemory` | Zero allocation for warmed construction and nullable retention, with exact field sums and the final observation preserved. |
| `NullableDebtEvidencePreservesJsonAndCapturedValues` | Exact nine-property JSON, null absence, and unchanged earlier observations after a later assignment. |
| `PeakSelectionRetainsFirstMaximumPerDebt` | Initialize both peaks, update each independently, and retain the first maximum through lower or equal observations. |

The reference measurement is 88 bytes per observation on the tested Windows runtime.
For 50,000 batches at probe interval 1024, the normal selected path performs 450,000 loop-probe service checks and 50,000 outer-loop checks.
Those 500,000 checks alone implied 44,000,000 bytes of reference-record allocation, before sample-triggered and other observations.
This is a source-derived allocation count. It is not a measured whole-process allocation total.

## Live Linux integration

The capture is `artifacts/debt-value-payment/20261016T053113515-8c895817`.

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 `
  -EvidenceDirectory artifacts/debt-value-payment -SelectedPayment -OwnerDrain `
  -DirectReceiptReady -FiniteMeasurement -SelectedCadence qualified-sample -SelectedBatches 50000
```

Zero speedup, 50-percent speedup, and expected invalidation after selected payment all pass with process exit zero.
Input hashes remain unchanged.
All four managed groups pass in Linux. The bound cooperative group includes the new allocation and JSON checks.
The native checks, retained analyzer checks, and live replay also pass.
The invalidation case verifies that the retained failure debt still reaches the existing JSON replay checks.
The new epoch-prefix version marker also reaches the retained analyzer in this capture.
The capture precedes extraction of the unchanged peak comparisons into `ObservePeaks` for direct checking.
The final Release build and pure cooperative group pass on Windows and Linux after that extraction.
The Linux log is `artifacts/debt-observation-final-linux-check.log`.
The live records do not retain every debt observation, so they cannot independently reconstruct the true maxima.
The direct production-helper check supplies that regression coverage.

The run does not isolate full-workload performance or establish prediction accuracy.
No failed run was discarded or repeated for a passing outcome.

## Accepted limits

The value record removes a heap allocation at construction, but its copies are larger than reference copies.
Peak and failure storage can increase closure size and copy work.
Serialization and other profiler operations still allocate.
The exact allocation check covers warmed construction on the tested runtimes, not every runtime configuration or serialization path.
No native API, source lifetime rule, sample-credit rule, or payment bound changes.

See [the observation reference](../../../docsSrc/Reference/bound-sampled-payment.md)
and [the overhead explanation](../../../docsSrc/Explanations/bound-observer-overhead.md).
