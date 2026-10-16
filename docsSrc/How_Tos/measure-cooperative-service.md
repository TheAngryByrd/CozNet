# How to measure recurring cooperative service

Prepare the Linux profiler and workload with the [bound payment procedure](run-bound-selected-payment.md).
Install Python 3 and prepare the repository container images.

1. Run the finite diagnostic from the repository root:

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 `
  -ProbeInterval 8192 `
  -EvidenceDirectory artifacts/recurring-service-timing `
  -PrepareReceipts -ServiceTiming `
  -SelectedPayment -OwnerDrain -DirectReceiptReady -FiniteMeasurement `
  -PauseTiming -DispatchTiming `
  -SelectedCadence qualified-sample -SelectedBatches 50000
```

2. Inspect `summary.json`, `analysis.json`, and all three case records in the reported directory.
3. Confirm that `serviceTimingEnabled` is true in the manifest and each case.
4. Compare `serviceTiming.Measurement` buckets for zero and 50-percent cases.
5. Divide `ElapsedTicks` by `timestampFrequency` to calculate seconds for each bucket.
6. Retain the expected invalidation case and all failed outcomes.

Use `Measurement` for measurement-window conclusions. Use `All` only when final draining belongs in the comparison.
Do not add pause totals to service totals because the intervals can overlap.
Do not rerun unchanged failures solely to obtain a pass.

See [the timing reference](../Reference/cooperative-service-timing.md) for bucket meanings and observer limits.
