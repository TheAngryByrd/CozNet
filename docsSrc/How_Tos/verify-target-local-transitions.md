# How to check target-local payment under forced GC

Use this procedure to check that target-local payment survives frequent blocking GCs.
It requires the .NET 10 SDK, PowerShell 7, Python 3, Docker, and the images `coznet-native-build:10.0.100` and `mcr.microsoft.com/dotnet/sdk:10.0.100`.

1. Run this command from the repository root:

   ```sh
   dotnet run --project build/build.fsproj -- -t TargetLocalTransitionsVerify
   ```

2. Open the new directory under `artifacts/target-local-transitions`.
3. Open `summary.json`. Check `passed` and `inputsUnchanged`.
4. For each entry in `outcomes`, check that `exitCode` is 0 and `passed` is true.
5. Check that each `forcedCollections` value is at least 10. It counts collections that start while a worker is working.
6. Check that each `maxHandlerNs` value is less than 5,000,000.
7. Check that each `lifecycle` value ending in `Passed` is true. They cover tiering rejection, binding, and shutdown.
8. Check that `cleanupPassed` is true. It shows that a run whose monitor throws still closes its experiment.
9. Check that `gcWindowsComplete` is true. Each forced collection must have one GC window, with none dropped.
10. For a failed run, open `gc-summary.json`. Read the windows that overlap each debt failure and the largest event gap of each worker.
11. Open `gc-<run>.json` for that run. Read `ledgerFailure`, `replayError`, `gcWindows`, and the events before the failure.

Ledger failure 1 is `debt_exceeded`. It means that a thread observed more than 2 ms of debt.
Do not repeat an unchanged run to obtain a pass. Keep the failed run as evidence.

The check does not measure prediction accuracy under GC. It does not cover JIT, tiering, ReJIT, thread abort, or process exit.
See [the transition evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/target-local/transitions.md) and [the API reference](../Reference/target-local-api.md).
