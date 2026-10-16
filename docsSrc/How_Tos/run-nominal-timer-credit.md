# How to capture nominal timer credit

Use PowerShell 7, Python 3, and the Linux x64 Docker environment for the sampled timer workload.
The local images must include `coznet-native-build:10.0.100` and `mcr.microsoft.com/dotnet/sdk:10.0.100`.

1. Build the workload:

```powershell
dotnet build tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj -c Release
```

2. Capture one declared run in a new evidence directory:

```powershell
pwsh -NoProfile -File native/VerifyTimerInterop.ps1 `
  -EvidenceDirectory artifacts/timer-nominal-credit `
  -SampledTimer -InitializeManagedPaths -InitializeNativeTransport `
  -ShortInitialInterval -NominalSampleCredit
```

3. Inspect `manifest.json`, `outcomes.json`, `summary.json`, and each `*.credit.json` replay report.
4. Confirm each case declares `nominal-event-v1`, evidence version `1`, and a 1,000,000 ns live period.
5. Retain every failed workload and replay result. Do not retry unchanged timing failures to obtain a pass.

The runner rejects missing records, policy mismatches, replay failures, and changed inputs.
A successful accounting replay does not establish native safety, source membership, capacity, or prediction accuracy.
The current matched-timer runner rejects nominal records.

See [the policy reference](../Reference/nominal-event-credit.md) and [the estimator explanation](../Explanations/nominal-event-credit.md).
