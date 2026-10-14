# How to check target-local timer delivery

Use this native Linux x64 fixture to check delivery between separate signal handlers.
It does not run the CLR or connect to the sampled service.

1. Install the .NET 10 SDK, PowerShell 7, and a Linux Docker engine.
2. Make `coznet-native-build:10.0.100` available locally.
3. Run this command from the repository root:

   ```sh
   dotnet run --project build/build.fsproj -- -t LinuxTargetLeaseDelivery
   ```

4. Open the new directory under `artifacts/target-lease-delivery`.
5. Inspect `summary.json` and all four workload records, including failures.
6. In `chain.json`, compare consecutive `progress` values to inspect application progress between handlers.
7. In `snapshot-chain.json`, check `snapshotConsistent`, `snapshotReads`, and each child's `firstNativeReceipt`.
8. In `pending-retirement.json`, check `pendingSeen`, `timerDeleted`, `drained`, and `restored`.
9. In `cancel-rearm.json`, check `rearmPendingSeen`, `disarmed`, `drainEmpty`, `rearmDrained`, and the single retained child.

The runner retains source hashes, a binary hash, the image ID, and all case exit codes.
It first runs portable lease contract checks. It runs each native case once and reports failure after all cases finish.
Do not repeat an unchanged timing case to obtain a pass.

The chain reserves 500,000 ns and requests at most 50,000 ns per handler.
Its 10,000 ns timer interval is not credited as intervention.
The target arms the first expiration with delivery blocked and a 1 ms setup interval.
It publishes lease state before it unblocks delivery.
In `chain`, the observer sleeps for 20 ms while the target rearms its own timer.
In `snapshot-chain`, it copies published prefixes while the target continues.
Receipt timestamps follow successful copies and stay fixed after the first copy of each child.
The fixture checks a 5 ms measured handler limit and a 100 ms first native observer receipt limit.
It does not check first managed receipt, sampled debt, forced GC, or physical prediction accuracy.
The existing limits for those behaviors remain unchanged.

The pending-retirement case blocks delivery on the target before it arms the timer.
It observes a pending signal, deletes the timer, and drains the signal before target exit.
The process restores its prior signal handler after the target joins.

The cancel-rearm case injects cancellation immediately after the first handler rearms its timer.
It waits for the blocked expiration, disarms the timer, and drains that expiration with direct Linux x64 syscalls.
The handler interval includes this cancellation work. Only the child's measured wait counts as intervention.
The case requires one completed child, one drained expiration, and no pending record at retirement.
It does not test concurrent cancellation, failed syscalls, or storage reuse.

See [the portable lease contract](../Reference/target-local-lease.md) and [the timer design and limits](../Research/target-local-timer-delivery.md).

Run `dotnet run --project build/build.fsproj -- -t ServiceAccountingVerify` for the portable managed receipt checks.
These checks use synthetic timestamps. They do not run timer-to-.NET interop.

Run `dotnet run --project build/build.fsproj -- -t ServiceOwnerVerify` for the portable lease and mailbox checks on the host.
Use `LinuxServiceOwnerVerify` for those checks in the Linux compiler container.
The mailbox checks include reader exclusion during reuse and generation exhaustion. They do not run the timer backend.
