# How to capture a finite cooperative measurement

Use the existing Linux container, native profiler, and Release workload build.
Run this command from the repository root:

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 -SelectedPayment -SelectedCadence qualified-sample -SelectedBatches 50000 -OwnerDrain -DirectReceiptReady -FiniteMeasurement -EvidenceDirectory artifacts/cooperative-measurement
```

1. Retain every case, including failed cases.
2. Inspect `measurement.Window` in each raw case record.
3. Compare its frozen values with the later `finalDebt` and payment records.
4. Inspect `measurementAssessment.Status` and every reason before using the descriptive values.
5. Check `analysis.json` for replay results and `summary.json` for workload outcomes.

An intended invalidation can pass the workload check and still produce an invalid measurement.
No current finite record qualifies a physical prediction comparison.
Do not retry an unchanged timing failure to obtain a pass.

See [the reference](../Reference/cooperative-measurement.md) for estimator inputs and qualification limits.
