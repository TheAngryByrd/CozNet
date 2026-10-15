# How to compare bound selected cadence

Prepare the Linux compiler and SDK container images used by the [selected-payment diagnostic](run-bound-selected-payment.md).
Install Python 3 for evidence replay.

1. Run the comparison:

   ```powershell
   dotnet run --project build/build.fsproj -- -t LinuxBoundSelectedCadence
   ```

2. Open `summary.json` in the reported `artifacts/bound-selected-cadence/` directory.
3. Inspect all four child runs, including failed runs.
4. Compare `0.json` and `50.json` across the continuous and qualified modes.
5. Check each invalidation record for rejected resume after selected payment.

The fixed order is continuous, qualified, qualified, continuous.
Each completed case executes 5,000 selected batches.
Each child run retains source copies, binaries' hashes, runtime evidence, and analysis results.
Failures do not stop the remaining comparison runs.
Do not repeat an unchanged failed comparison solely to obtain a pass.

Use completed records to compare selected work duration, sample coverage, pauses, receipts, and peer progress.
Keep failed capacity and reach outcomes beside those measurements.
The comparison does not isolate each observer cost or establish prediction accuracy.
See [the reference](../Reference/bound-selected-cadence.md) for admission and schema contracts.

## Compare owner-driven collection

After the target builds the required binaries, capture the background baseline:

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 -SelectedPayment -ChildCpu -SelectedCadence qualified-sample -SelectedBatches 10000 -EvidenceDirectory artifacts/owner-drain-background
```

Capture the owner-driven case with the same work and diagnostics:

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 -SelectedPayment -ChildCpu -OwnerDrain -SelectedCadence qualified-sample -SelectedBatches 10000 -EvidenceDirectory artifacts/owner-drain-owner
```

Run the commands serially. Keep every failed case.
Check `ownerDrainEnabled` in each runtime record and its manifest.
Compare completed work duration, samples, pauses, and native receipts at the same batch count.
Do not infer prediction accuracy from a capacity pass.
See [the collector reference](../Reference/pausable-sample-collector.md#owner-driven-collection) for ownership and failure behavior.
