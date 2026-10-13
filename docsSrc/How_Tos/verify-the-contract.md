# Verify the deterministic contract

From the repository root:

```sh
dotnet test tests/CozNet.Tests/CozNet.Tests.fsproj -c Release
```

These tests verify parsing, accounting, scheduling, and session lifecycle.
They do not establish physical prediction accuracy. Use the [sample](../Tutorials/Getting_Started.md) for that investigation.

For the sampled Linux prototype, run:

```sh
dotnet run --project build/build.fsproj -- -t LinuxPredictionVerify
```

This target requires PowerShell 7, a Linux Docker engine, and the local `mcr.microsoft.com/dotnet/sdk:10.0.100` image.
It compares eight randomized blocks with an equivalent physical optimization.
It fails when the 95-percent paired-error interval exceeds two percentage points.
Inspect `artifacts/linux-prediction/last-run.txt` for the evidence directory, including failed results.
The shared checks and physical workload use portable .NET APIs. The sampler and native clock require Linux x64.
This runner has been verified from a Windows host. Other host platforms remain unverified.
