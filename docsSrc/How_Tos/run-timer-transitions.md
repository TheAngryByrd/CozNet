# How to check timer delivery during runtime transitions

Use this procedure to check the experimental Linux x64 timer backend during JIT compilation, waits, and managed/native transitions.
You need PowerShell, the repository SDK, a Linux Docker engine, and these local images:

- `coznet-native-build:10.0.100`
- `mcr.microsoft.com/dotnet/sdk:10.0.100`

1. Run this command from the repository root:

   ```powershell
   dotnet run --project build/build.fsproj -- -t LinuxTimerTransitions
   ```

2. Open the reported directory under `artifacts/timer-transitions/`.
3. Check `summary.json` for all eight scenario outcomes and the layout outcome.
4. Check `inputsUnchanged` is `true`.
5. Open each scenario's JSON record and check `passed`, `joined`, `closed`, and `stableMap`.
6. Check the scenario's reach conditions in [the workload reference](../Reference/timer-transitions.md).
7. Check each final journal against its command's `Confirmed` wait total.
8. For GC cases, check `gcReachedCommand`, `gcJoined`, and each collection's duration.
9. Check `selectedRetired`, `selectedJoined`, and `selectedReleased` are `true`.
10. For transition cases, check `selectedPayments` contains a positive `Confirmed` wait.
11. Sum each selected payment's final child waits and compare the result with `Confirmed`.
12. Retain failed records and logs. Do not repeat an unchanged timing case to obtain a pass.

The runner executes all eight cases even if an earlier case fails.
Each case has a separate process and a 20-second watchdog.
A failed case makes the target fail after the runner writes the summary.
See [the workload reference](../Reference/timer-transitions.md) for record fields, bounds, and interpretation limits.

## Check 10 µs initial delivery

Run the short-interval matrix:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxFastTimerTransitions
```

Apply the same checks to all eight records.
Confirm that each record and the manifest report `initialIntervalNanoseconds = 10000`.
This matrix checks controlled runtime transitions. It does not establish sampled forced-GC capacity or prediction accuracy.
