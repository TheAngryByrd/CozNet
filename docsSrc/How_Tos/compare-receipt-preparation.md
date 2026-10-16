# How to compare managed receipt preparation

Use this procedure to compare the optional preparation step in the controlled Linux sampled workload.
Use the same build, image, work count, probe interval, and diagnostic options for each group.
The [bound selected-payment procedure](run-bound-selected-payment.md) describes the native build and container prerequisites.

1. Build the managed workload in Release mode.

   ```powershell
   dotnet build tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj -c Release
   ```

2. Capture an unprepared group.

   ```powershell
   pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 `
     -EvidenceDirectory artifacts/receipt-unprepared -ProbeInterval 8192 `
     -SelectedPayment -OwnerDrain -DirectReceiptReady -FiniteMeasurement `
     -PauseTiming -DispatchTiming -SelectedCadence qualified-sample -SelectedBatches 50000
   ```

3. Capture a prepared group with the same options and `-PrepareReceipts`.
   Use a separate evidence directory.

4. Capture another prepared group, followed by another unprepared group.
   Retain every outcome, including failed measurements.

5. Check the manifests for identical source hashes, managed binaries, and image identity.
   Check each summary's `inputsUnchanged`, expected outcomes, and requested preparation mode.

6. Inspect peer submit, read, and release records for command 1 in the dispatch prefix.
   Compare read-action brackets and full request brackets separately.
   Check `receiptPreparation` and its completion before measurement in prepared records.

The preparation bracket is setup evidence. Do not add it to the measured program time.
Lower first-request cost does not establish lower total overhead or accurate prediction.
See [the preparation contract](../Reference/cooperative-managed.md#optional-managed-receipt-preparation)
and [the cost explanation](../Explanations/cooperative-pause-cost.md).
