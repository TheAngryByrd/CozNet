# Bound sample clock capture

Capture: `artifacts/bound-sample-clock/20261016T112911781-1e9fd18d`.

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 -SelectedPayment -OwnerDrain -SelectedCadence qualified-sample -FiniteMeasurement -BoundSampleClock -EvidenceDirectory artifacts/bound-sample-clock
```

The declared three-case Linux run passes with unchanged source and binary hashes.
Zero speedup and 50-percent speedup complete. Expected invalidation passes with `completed=false`.
All three captures retain two clock readings, stable namespace identity, zero namespace offsets, and matching frequencies.
Both native readings lie inside their managed brackets. The brackets enclose the frozen measurement endpoints.
Replay reports `consistent` for all three cases.

All four managed preflight groups pass in the Linux container.
The seven clock replay tests, 32 peer replay tests, 109 selected replay tests, and PowerShell outcome checks pass on the host.
Host managed clock, bound cooperative, and pausable collection checks pass.
The controlled source verifies first-read ordering before enable and cleanup after backend failure.

The run uses .NET 10.0.0, Linux x64, and kernel `5.15.153.1-microsoft-standard-WSL2`.
It does not establish cross-platform live clock support, sample membership, exact attribution, or prediction accuracy.
Measured wall time remains authoritative payment. All timing bounds remain unchanged.

See [the clock reference](../../../docsSrc/Reference/cpu-sample-clock.md#bound-sampled-evidence).

Review follow-up checks pass with 8 clock replay tests, 33 peer replay tests, and 110 selected replay tests.
The managed checks also pass after the wrapper rejects misuse before source creation.
The final analyzer replays the retained capture without changing its input files.
These follow-up changes do not have a second live timing capture.
