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
It does not test concurrent cancellation, injected syscall failures, CLR execution, managed receipt, or sampled debt capacity.

See [the mailbox contract](../Reference/target-local-lease.md#lease-mailbox) and [the timer reuse design](../Research/target-local-timer-delivery.md#reusing-one-timer-across-leases).
