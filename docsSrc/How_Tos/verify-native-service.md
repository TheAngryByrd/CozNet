# How to verify native service accounting

Use this procedure to check native journal settlement and rejection.
Run commands from the repository root with the .NET 10 SDK.
The Linux checks also require PowerShell 7 and a Linux Docker engine.
They require these local images:

- `coznet-native-build:10.0.100`
- `mcr.microsoft.com/dotnet/sdk:10.0.100`

1. Run the portable peer and selected adapter checks:

   ```sh
   dotnet run --project build/build.fsproj -- -t ServiceAccountingVerify
   ```

   The checks exercise journal validation, receipt bounds, stale states, settlement, and replay rejection.

2. Run the peer service interop checks on Linux:

   ```sh
   dotnet run --project build/build.fsproj -- -t LinuxServiceInteropVerify
   ```

3. Run the selected service interop checks on Linux:

   ```sh
   dotnet run --project build/build.fsproj -- -t LinuxSelectedServiceInteropVerify
   ```

   Each Linux run creates a unique directory under `artifacts/service-interop/`.
   Each managed child has a 30-second watchdog.

4. Inspect `summary.json` and `outcomes.json` in each run directory.
   Confirm that all recorded outcomes pass and that source and binary hashes remain unchanged.

5. Inspect `receipt.json` and `late.json` alongside their logs.
   Confirm that the normal fixture settles and that the deliberately late fixture rejects accounting.
   Check the recorded target joins and session closure.

6. Retain failed runs and their `manifest.json` files.
   Investigate failures before changing code or repeating a timing workload.

A passing late fixture requires rejection. It does not authorize acceptance of late observations.
These checks do not establish sampled capacity or physical prediction accuracy.

See the [accounting reference](../Reference/native-service-accounting.md) for limits.
The [retained selected results](../../reliability/features/native-signal/selected-service/README.md) show the expected normal and late outcomes.
