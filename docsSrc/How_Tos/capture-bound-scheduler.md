# How to capture bound scheduler evidence

Use this diagnostic to compare peer progress queries with observed thread switch-out intervals.
The Linux container, native profiler, and built workload must be available.

1. Run the selected-payment workload with scheduler capture:

   ```powershell
   pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 -SelectedPayment -PeerProgress -Scheduler -SelectedCadence qualified-sample -OwnerDrain -DirectReceiptReady
   ```

2. Open `summary.json` in the reported evidence directory.
3. Check each case outcome and retain failed cases.
4. Open `scheduler-analysis-summary.json` and the corresponding `<case>-scheduler.json` files.
5. Require `evidenceVerified: true` before interpreting overlap bounds.
6. Compare each marker with `markerQueryStarted` and `markerQueryFinished`.

The runner retains source snapshots, hashes, raw records, and analysis results.
A valid scheduler analysis does not turn a failed workload into a successful experiment.
Zero speedup can produce no peer query windows.
The analyzer reports this absence explicitly.

See [the capture reference](../Reference/bound-scheduler-capture.md) for clock assumptions and censored interval limits.
