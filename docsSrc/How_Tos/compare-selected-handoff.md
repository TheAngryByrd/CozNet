# How to compare selected scheduling handoff

Use this procedure to test the experimental scheduling policy in the bound workload.
The Linux container, native profiler, and built workload must be available.
Keep the same machine configuration and batch count for both commands.

1. Capture the default policy:

   ```powershell
   pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 -SelectedPayment -SelectedCadence qualified-sample -SelectedBatches 50000 -OwnerDrain -DirectReceiptReady -PeerProgress -Scheduler -EvidenceDirectory artifacts/handoff-off
   ```

2. Capture the handoff policy:

   ```powershell
   pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 -SelectedPayment -SelectedCadence qualified-sample -SelectedBatches 50000 -OwnerDrain -DirectReceiptReady -PeerProgress -Scheduler -SelectedHandoff -EvidenceDirectory artifacts/handoff-on
   ```

3. Retain every case and its failure, including cases that fail before the intended invalidation.
4. Compare `summary.json`, `analysis.json`, and the raw case records from both directories.
5. Check `selectedHandoffAttempts` in each case. Zero speedup must have zero attempts.
6. Check that each manifest reports unchanged source and binary inputs through its corresponding summary.

A treatment with no handoff attempts does not exercise the new scheduling operation.
Passing capacity gates does not establish prediction accuracy or an overhead benefit.
Do not retry an unchanged timing failure to obtain a pass.

See [the handoff reference](../Reference/selected-handoff.md) for prerequisites, accounting rules, and prediction limits.
