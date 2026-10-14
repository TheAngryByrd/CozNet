# How to check target-local timer reuse

Use this native Linux x64 fixture to check repeated generations in one timer registration.
It requires the .NET 10 SDK, PowerShell 7, Docker, and the local `coznet-native-build:10.0.100` image.

1. Run this command from the repository root:

   ```sh
   dotnet run --project build/build.fsproj -- -t LinuxTargetLeaseReuse
   ```

2. Open the new directory under `artifacts/target-lease-reuse`.
3. Inspect `summary.json`, `contracts.log`, and `reuse.json`, including failed results.
4. Check that `completed` is 16 and each generation has one child with a matching command.
5. Check that every generation has retired `visibility` value 3.
6. Check every `reuseRejected` value and the disarm and drain results.
7. Check `closed`, `timerDeleted`, `restored`, and `retiredDrained` after target join.

The runner retains source hashes, the binary hash, image identity, and exit codes.
It runs the portable mailbox and lease checks before one native reuse case.
Do not repeat an unchanged timing case to obtain a pass.

Each generation requests one 50,000 ns child through a 1 ms one-shot expiration.
The handler disarms, drains, and retires the core before publishing mailbox quiescence.
The controller holds a reader guard while copying the journal and checks that a reuse attempt fails.
This attempt does not isolate reader exclusion because mailbox quiescence can still be pending.
The portable mailbox checks separately verify reader exclusion after mailbox quiescence.
The controller requires a retired snapshot before releasing the guard.
It then begins the next generation and reconstructs the lease in the same storage slot.
The first native receipt follows the first successful child copy. Later copies cannot replace that timestamp.

This fixture checks a 5 ms measured handler bound and a 100 ms first native receipt bound.
The `reuse` mode does not test concurrent cancellation, syscall failures, CLR execution, managed receipt, or sampled debt capacity.

## Check cancellation before delivery

1. Run `dotnet run --project build/build.fsproj -- -t LinuxTargetLeaseCancellation` from the repository root.
2. Open the new evidence directory and inspect `reuse.json`.
3. Check that `mode` is `cancel-before-delivery` and `completed` is 16.
4. Check that odd generations have accepted cancellation, stop value 3, and zero measured wait.
5. Check that even generations have stop value 1 and at least 50,000 ns of measured wait.
6. Check retired visibility and final teardown fields as in the reuse procedure.

The controller cancels odd generations during configuration, before publication and timer arming.
Each even generation must complete normally after the preceding cancelled generation.
This checks cancellation preservation and reset across native delivery. It does not test cancellation during an active handler.

## Check rejected initial arms

1. Run `dotnet run --project build/build.fsproj -- -t LinuxTargetLeaseArmRecovery` from the repository root.
2. Inspect `kernel.txt`, `summary.json`, `contracts.log`, and `reuse.json` in the new evidence directory.
3. Check that `mode` is `reject-initial-arm` and `completed` is 16.
4. Check that odd generations have `armResult` -22, `recoveryAcquired` true, and `gettimeResult` zero.
5. Check that all four remaining timer fields are zero for those generations.
6. Check that they have zero children, zero measured wait, stop value 5, error value 1, and `transportError` 22.
7. Check that their `headerCaptured` is positive, while `captured` and `firstNativeReceipt` are zero.
8. Check that each even generation completes one child and passes the existing timing bounds.
9. Check retired visibility and final teardown fields as in the reuse procedure.

The [rejection contract](../Reference/target-local-lease.md#lease-mailbox) states the required quiescence proof and kernel-source limitation.
This mode does not cover ambiguous syscall failures or cancellation during an active handler.

See [the mailbox contract](../Reference/target-local-lease.md#lease-mailbox) and [the timer reuse design](../Research/target-local-timer-delivery.md#reusing-one-timer-across-leases).
