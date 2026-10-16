# How to capture selected-thread CPU

Prepare the Linux profiler, managed workload, and container images with the [comparison procedure](capture-cooperative-comparison.md).
Use PowerShell 7 and Python 3.

1. Run a four-arm comparison:

```powershell
pwsh -NoProfile -File native/VerifyCooperativeComparison.ps1 `
  -EvidenceDirectory artifacts/selected-cpu-comparison `
  -SelectedBatches 50000 -ProbeInterval 8192 `
  -PrepareReceipts -OwnerController -NominalSampleCredit -SelectedCpu
```

2. Inspect every arm, `analysis.json`, and the integrity and runner error records.
3. Confirm that every arm reports `selectedCpuEnabled=true` and a complete CPU capture.
4. Compare CPU advance with the elapsed bounds in each case's `selectedCpu` result.
5. Retain failures. Do not retry unchanged timing cases solely to obtain a pass.

The runner returns nonzero while prediction remains unqualified.
CPU observations do not qualify the causal estimate.
See [the reference](../Reference/selected-cpu.md) for partial captures and [the explanation](../Explanations/selected-cpu.md) for interpretation limits.
