# How to capture nominal-event credit

Prepare the Linux profiler, workload, and images with the [bound selected-payment procedure](run-bound-selected-payment.md).
Use PowerShell 7 and Python 3.

1. Run zero, treatment, and expected invalidation cases:

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 `
  -EvidenceDirectory artifacts/nominal-event-credit `
  -SelectedPayment -OwnerDrain -DirectReceiptReady -FiniteMeasurement `
  -PrepareReceipts -OwnerController -NominalSampleCredit `
  -SelectedCadence qualified-sample -SelectedBatches 50000 -ProbeInterval 8192
```

2. Inspect every outcome, `summary.json`, `manifest.json`, and the replay results.
3. Confirm `sampleCreditPolicy="nominal-event-v1"` and `nominalSamplePeriodNanoseconds=1000000` in each case record.
4. Retain failed cases. Do not retry an unchanged failure solely to obtain a pass.
5. After review, capture the physical comparison:

```powershell
pwsh -NoProfile -File native/VerifyCooperativeComparison.ps1 `
  -EvidenceDirectory artifacts/nominal-physical-comparison `
  -SelectedBatches 50000 -ProbeInterval 8192 `
  -PrepareReceipts -OwnerController -NominalSampleCredit
```

The comparison runner returns nonzero while prediction remains unqualified.
Inspect arm outcomes and integrity separately from qualification.
See [the policy reference](../Reference/nominal-event-credit.md) and [comparison procedure](capture-cooperative-comparison.md).
