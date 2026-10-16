# How to capture drain and service CPU

Prepare the Linux profiler, managed workload, and container images with the [comparison procedure](capture-cooperative-comparison.md).
Use PowerShell 7 and Python 3.

1. Run the component diagnostic:

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 `
  -EvidenceDirectory artifacts/component-cpu `
  -SelectedPayment -OwnerDrain -FiniteMeasurement `
  -SelectedCadence qualified-sample -SelectedBatches 50000 -ProbeInterval 8192 `
  -DirectReceiptReady -OwnerController -PrepareReceipts -NominalSampleCredit -ComponentCpu
```

2. Inspect `summary.json` for case outcomes and unchanged input hashes.
3. Inspect each case's `componentCpu.Measurement` buckets.
4. Check `Failure` and `Active` before interpreting any CPU totals.
5. Retain failed cases and their replay results.

The runner binds capture activation to every case.
The expected invalidation case can retain failed drain or service calls.
CPU clock reads change workload cost and can cause new capacity failures.
These observations do not establish prediction accuracy.

See [the field reference](../Reference/cooperative-component-cpu.md) for activity definitions and measurement limits.
