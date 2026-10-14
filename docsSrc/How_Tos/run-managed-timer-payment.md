# How to check timer payment during managed execution

Use this procedure to check paid timer waits against a controlled managed arithmetic method.
You need the repository SDK, PowerShell, a Linux Docker engine, and these local images:

- `coznet-native-build:10.0.100`
- `mcr.microsoft.com/dotnet/sdk:10.0.100`

1. Run this command from the repository root:

   ```powershell
   dotnet run --project build/build.fsproj -- -t LinuxTimerManaged
   ```

2. Open the reported directory under `artifacts/timer-managed/`.
3. Check `summary.json` for successful layout and managed outcomes, with `inputsUnchanged: true`.
4. Check `managed.json` for `passed`, `joined`, `closed`, and `stableMap` set to `true`.
5. Check that `chainedLeases` is positive and `paidChildren` exceeds one.
6. Inspect both saved perf maps for the selected `CpuWorkload::Run()[Optimized]` range.
7. Check each final snapshot for paid instruction addresses inside that range.
8. Sum each final journal's child waits and compare the result with its command's `Confirmed` value.
9. Check that `ProgressAfter` exceeds `ProgressBefore` for every submitted command.
10. Check `pendingAccounting` is zero.

The workload uses 16 fixed synthetic-credit steps and one target registration.
Each step adds 110,000 ns of selected credit.
If earlier wait overshoot covers the new credit, the workload records a skipped step instead of submitting another lease.
For an independent budget check, add each step's credit and subtract each submitted command's confirmed wait.
Each submitted budget must equal the positive balance before its wait.

The runner disables tiering, ReadyToRun, and startup profiling.
It captures the exact perf-map bytes before and after intervention.
The requested child wait remains at most 50,000 ns.
The handler limit remains 5 ms, and the first managed receipt limit remains 100 ms.

Retain every failed record and log. Do not repeat an unchanged timing case to obtain a pass.
This procedure does not check forced GC, general CLR safety, sampled capacity, or physical prediction accuracy.
Progress counters bracket controller submission and final receipt processing.
They do not prove progress within the native lease or between every pair of handlers.

See [timer journal accounting](../Reference/timer-accounting.md) for settlement rules.
See [the retained payment evidence](../../reliability/features/native-signal/timer-managed.md) for measured results and their limits.
