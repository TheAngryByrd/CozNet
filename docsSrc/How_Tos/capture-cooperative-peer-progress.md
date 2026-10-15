# How to capture cooperative peer progress

Use this diagnostic to retain native peer phases during selected-payment experiments.
The Linux container, native profiler, and workload prerequisites must be available.

1. Run the selected-payment workload with progress capture enabled:

   ```powershell
   pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 -SelectedPayment -PeerProgress
   ```

2. Open the generated case JSON files in the reported evidence directory.
3. Inspect `peerSubmitProgress` and `peerDebtProgress` with their request identities.
4. Keep failed cases and their original outcomes.

A successful query identifies the last recorded phase within its query bracket.
A missing pending request or failed query remains explicit in the evidence.
The runner checks activation and replays the selected-payment record.
This diagnostic does not establish stable capacity or scheduling cause.

See the [marker reference](../Reference/cooperative-progress-marker.md) for fields and phase limits.
