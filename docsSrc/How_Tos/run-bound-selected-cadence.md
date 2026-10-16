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

## Run the lower-density probe experiment

Rebuild the managed workload and Linux profiler:

```powershell
dotnet build tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj -c Release
pwsh -NoProfile -File native/BuildLinux.ps1
```

Run the opt-in interval with the existing measurement and payment bounds:

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 -ProbeInterval 8192 -SelectedPayment -OwnerDrain -DirectReceiptReady -FiniteMeasurement -SelectedCadence qualified-sample -SelectedBatches 50000 -EvidenceDirectory artifacts/sparse-probe-payment
```

Check the manifest's `interval` and each runtime record's `probeInterval` against `8192`.
Keep zero-speedup, treatment, and invalidation records, including failures.
Use `-ProbeInterval 1024` for the existing density with the same settings.
Do not infer an isolated speedup from separate runs or prediction accuracy from a capacity pass.
See [the interval contract](../Reference/bound-sampled-payment.md#probe-interval).

Run the four-arm physical comparison at the same interval:

```powershell
pwsh -NoProfile -File native/VerifyCooperativeComparison.ps1 -ProbeInterval 8192 -SelectedBatches 50000 -EvidenceDirectory artifacts/sparse-probe-comparison
```

Inspect every arm and `analysis.json`. The runner returns failure while prediction remains unqualified, even when all processes complete.
See [the comparison reference](../Reference/cooperative-comparison.md).

## Compare peer service after sample batches

After the target builds the required binaries, run both publication modes with the same settings:

```powershell
$modes = @($false, $true, $true, $false)
foreach ($batch in $modes) {
    pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 -SelectedPayment -OwnerDrain -ChildCpu -PauseTiming -SelectedCadence qualified-sample -SelectedBatches 50000 -BatchService:$batch -EvidenceDirectory artifacts/batch-service
}
```

Run the cases serially and retain every result, including failures.
Check `batchServiceEnabled` against the declared order in each manifest and runtime record.
Compare source and binary hashes before attributing a difference to publication mode.
Compare completed work duration, processed samples, and `sampleServiceCalls` at the same work count.
Keep incomplete cases separate. A smaller service count does not establish lower controller cost or stable capacity.
See [publication semantics](../Reference/bound-sampled-payment.md#sample-publication-cadence) and [capacity limits](../Explanations/owner-drain-capacity.md).

## Capture controller dispatch intervals

After the target builds the required binaries, enable the dispatch diagnostic:

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 -SelectedPayment -OwnerDrain -ChildCpu -PauseTiming -DispatchTiming -SelectedCadence qualified-sample -SelectedBatches 50000 -EvidenceDirectory artifacts/controller-dispatch
```

Check `dispatchTimingEnabled` and the retained source and binary hashes.
Inspect `dispatchTiming.Recent` near the failed request, then compare its four timestamps with the matching failure boundary.
Check the operation label, command, and generation before combining records.
Keep queue, controller action, and return intervals separate from native child waits.
Treat absent start or finish timestamps as incomplete evidence.
Check whether a maximum belongs to creation or cleanup before using it to describe payment.
Retain all failures. The enabled observer changes execution cost.
See [the reference](../Reference/cooperative-managed.md#controller-dispatch-timing) for interpretation limits.
