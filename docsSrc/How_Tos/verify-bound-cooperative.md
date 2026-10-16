# How to verify CLR-bound cooperative payment

Install the repository SDK and the native build prerequisites.
For Linux containers, prepare the existing compiler and SDK images.

1. Run the Windows x64 target:

   ```powershell
   dotnet run --project build/build.fsproj -- -t BoundCooperativeVerify
   ```

2. Run the Linux x64 container target:

   ```powershell
   dotnet run --project build/build.fsproj -- -t LinuxBoundCooperativeVerify
   ```

3. Inspect `summary.json`, `manifest.json`, and the twenty `bound-*.json` records in the reported directory.

Each target builds its profiler and managed workload before verification.
For controller-owned checks, use `BoundOwnerControllerVerify` and `LinuxBoundOwnerControllerVerify` with the same commands.
Confirm that each resulting record has `ownerControllerEnabled=true`.
The summary must report unchanged inputs and twenty successful case checks.
The paid cases must complete two generations at loop probe 2, then finish cleanup and close successfully.
The invalidation cases must reject experiment completion and still complete cleanup.
Before-submit cases must create no native generation.
Before-poll cases must retain one cancelled zero-wait child during cleanup.
Before-receipt cases must retain paid cleanup evidence without settling the ledger reservation.
After-settlement cases must retain settlement but reject final completion.

Retain failed records. Do not repeat unchanged timing failures to obtain a pass.
These checks do not run a sampled experiment or establish prediction accuracy.

See [the reference](../Reference/bound-cooperative.md) for admission and ownership rules.
