# Managed timer payment with forced GC

The optional GC mode uses the existing `SampledGcStress` worker while the timer payment workload runs.
The worker starts blocked. The controller releases it after the first native submission succeeds.
All timer, accounting, mapping, and checksum checks remain active.

## Retained evidence

- Base revision: `76796e88d86656bca1c8bf8a506ac4e011e3cd03`, with changes identified by each run's source hashes.
- GC command: `dotnet run --project build/build.fsproj -- -t LinuxTimerManagedGc`.
- GC evidence: `artifacts/timer-managed-gc/20261014T210255142-9722abf3`.
- No-GC regression command: `dotnet run --project build/build.fsproj -- -t LinuxTimerManaged`.
- No-GC evidence: `artifacts/timer-managed/20261014T210436436-956b084b`.
- Portable Windows checks: `TimerLayoutVerify`, recorded in `artifacts/timer-gc-host.log`.
- Target framework: .NET 10. Manifests identify container images, kernel details, sources, and binaries.
- Fixed workload: 16 synthetic-credit steps, each adding 110,000 ns. No randomized input is used.

| GC-run observation | Result |
| --- | --- |
| Submitted timer generations | 16 |
| Completed multi-child leases | 14 |
| Deferred leases | 2 |
| Paid children | 46 |
| Total measured wait | 1,760,039 ns |
| Final credit minus measured wait | -39 ns |
| Largest measured handler interval | 55,476 ns |
| Largest first managed receipt bound | 7,027,583 ns |
| Forced collection calls | 3 |
| Collections contained in command intervals | 2 |
| Largest collection call | 3.776224 ms |
| Final pending accounting | Zero |
| GC join, target join, and session close | Passed |

Collection 1 fits inside generation 1's command interval. Collection 2 fits inside generation 2's interval.
Their call durations are 3.776224 ms and 0.294987 ms.
Collection 3 lasts 0.408093 ms and does not establish command-interval reach.
All three calls satisfy the unchanged 250 ms bound.

Independent inspection reproduced each reserved budget, child wait sum, native aggregate, accounting total, and timing bound.
Generation 1 measured 50,002 ns before deferral. Generation 11 deferred with zero wait.
The following budgets retained that unpaid debt.
The no-GC regression run completed 16 multi-child leases with 48 paid children and no pending accounting.
Both runs retained stable code maps and correct arithmetic checksums.

## Reach and limits

Both interval endpoints and GC observations use `Stopwatch` timestamps.
A command interval starts after native submission returns and ends after final receipt processing.
The reach predicate requires one complete positive-index collection inside one interval.
It rejects a collection that occurs between commands or spans multiple intervals.
Portable checks cover exact endpoints, invalid intervals, missing observations, and malformed collection records.
The shared GC checks retain the inclusive 250 ms call limit and cancellation-before-release behavior.

The command interval includes controller overhead and scheduling delay.
Containment does not prove that GC overlapped an individual native wait or a runtime suspension.
These runs do not establish general CLR safety, sampled debt capacity, or physical prediction accuracy.
The 50,000 ns requested child wait, 5 ms handler, 100 ms first receipt, and 2 ms lease-budget limits remain unchanged.

See [the procedure](../../../docsSrc/How_Tos/run-managed-timer-payment.md#check-forced-gc) and [the original managed payment evidence](timer-managed.md).
