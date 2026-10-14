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

The workload uses 16 fixed synthetic-credit steps and two target registrations.
The second arithmetic target pays selected-side debt caused by peer overshoot.
Check `selectedRetired`, `selectedJoined`, and `selectedReleased` are `true`.
Check `selectedPayments` contains a positive confirmed wait and inspect its final child journals.
Each step adds 110,000 ns of selected credit.
If earlier wait overshoot covers the new credit, the workload records a skipped step instead of submitting another lease.
For an independent budget check, add each step's credit and all earlier selected payments, then subtract earlier peer payments.
Each submitted budget must equal the positive balance before its wait.

The runner disables tiering, ReadyToRun, and startup profiling.
It captures the exact perf-map bytes before and after intervention.
The requested child wait remains at most 50,000 ns.
The handler limit remains 5 ms, and the first managed receipt limit remains 100 ms.

Retain every failed record and log. Do not repeat an unchanged timing case to obtain a pass.
The default target does not force GC. Neither mode establishes general CLR safety, sampled capacity, or physical prediction accuracy.
Progress counters bracket controller submission and final receipt processing.
They do not prove progress within the native lease or between every pair of handlers.

## Check forced GC

1. Run `dotnet run --project build/build.fsproj -- -t LinuxTimerManagedGc` from the repository root.
2. Open the reported directory under `artifacts/timer-managed-gc/`.
3. Apply the payment and cleanup checks above.
4. Check `forceGc`, `gcReleased`, `gcJoined`, and `gcReachedCommand` are `true` in `managed.json`.
5. Check `gcError` is `null` and `collections` contains at least one record.
6. Find one collection contained in a command's `Interval`.
7. Check its `StartedTimestamp` is at least the command's `SubmittedTimestamp`.
8. Check its `FinishedTimestamp` is at most the command's `ReceivedTimestamp` and at least its own start.
9. Check its `CollectionIndex` is positive.
10. Calculate each call duration as `1000 * (FinishedTimestamp - StartedTimestamp) / stopwatchFrequency`.
11. Check every call duration is at most 250 ms.

The GC worker starts before command submission but remains blocked until the first submission succeeds.
Each command interval starts after native submission returns and ends after final receipt processing.
The interval includes controller overhead and possible scheduling delays.
A contained collection does not prove overlap with an individual native wait or a runtime suspension.
Collections between commands or across command boundaries do not satisfy this reach check.

If a run fails, inspect `unfinishedInterval`, `unfinishedSnapshots`, `lastRaw`, and the GC records.
An unjoined GC worker has no accepted observation snapshot.
See [the forced-GC evidence](../../reliability/features/native-signal/timer-managed-gc.md) for retained results and scope.

See [timer journal accounting](../Reference/timer-accounting.md) for settlement rules.
See [the retained payment evidence](../../reliability/features/native-signal/timer-managed.md) for measured results and their limits.
