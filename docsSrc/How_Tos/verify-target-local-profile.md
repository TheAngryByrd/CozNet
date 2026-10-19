# How to check a target-local causal profile

Use this procedure to check that a causal profile from `TargetCausalProfile.run` matches real optimizations.
It requires the .NET 10 SDK, PowerShell 7, Python 3, Docker, and the images `coznet-native-build:10.0.100` and `mcr.microsoft.com/dotnet/sdk:10.0.100`.

1. Run this command from the repository root:

   ```sh
   dotnet run --project build/build.fsproj -- -t TargetLocalProfileVerify
   ```

2. Open the new directory under `artifacts/target-local-profile`.
3. Open `summary.json`. Check `passed` and `inputsUnchanged`.
4. For each entry in `points`, check that `validBlocks` is 5 and `passed` is true.
5. For a failed point, open `blocks` and read `reasons`, `points`, and `order` for each block.
6. For a failed experiment, open `<block>-profile.json` and read `experiments`.
7. To check the throughput progress point, run `TargetLocalThroughputVerify` and open the new directory under `artifacts/target-local-throughput`. Repeat steps 3 to 6.
8. To check a pipeline with blocking waits, run `TargetLocalPipelineVerify` and open the new directory under `artifacts/target-local-pipeline`. Repeat steps 3 to 6.
9. To check workers that contend for a lock, run `TargetLocalLockVerify` and open the new directory under `artifacts/target-local-lock`. Repeat steps 3 to 6.
10. To check the same workload with a 1 ms spin, run `TargetLocalSpinLockVerify` and open the new directory under `artifacts/target-local-spinlock`. Repeat steps 3 to 6.
11. To check two workers that take turns through semaphores, run `TargetLocalSemaphoreVerify` and open the new directory under `artifacts/target-local-semaphore`. Repeat steps 3 to 6.
12. To check a latency progress point, run `TargetLocalTransactionVerify` and open the new directory under `artifacts/target-local-transaction`. Repeat steps 3 to 6.
13. To check the same workload with a consumer that spins up to 1 ms, run `TargetLocalSpinTransactionVerify` and open the new directory under `artifacts/target-local-spintransaction`. Repeat steps 3 to 6.
14. To pin each worker to one CPU, run `VerifyTargetLocalProfile.ps1` with `-WorkerCpus '0,2'` and a lock or transaction workload. The runner accepts only `'0,2'`, one CPU on each physical core. Open the new directory under `artifacts/target-local-<workload>-pinned`. Repeat steps 3 to 6.
15. To sample every 0.25 ms, run `VerifyTargetLocalProfile.ps1` with `-PeriodNs 250000` and a transaction workload. The runner accepts only `250000`. Open the new directory under `artifacts/target-local-<workload>-period`. Repeat steps 3 to 6. This run is a diagnostic, not a qualification run.
16. To compare peer work 8 and 2 at the `selected` points in the same blocks, run `TargetLocalBottleneckVerify`. Open the new directory under `artifacts/target-local-bottleneck`, and read `outcome` in `summary.json`. This run is a diagnostic, not a qualification run.
17. To add a fixed probe before each arm of that diagnostic, run `VerifyTargetLocalBottleneck.ps1 -Probe`. Open the new directory under `artifacts/target-local-bottleneck-probe`, and read `probe-summary.json`. The summary is descriptive only.

In the qualification suites, each block runs one profile process and seven plain processes in a random order.
The profile process runs a baseline and six experiments: `Work.selected` and `Work.peer` at speedups 25, 50, and 75.
The plain processes run the original workload and one optimized workload for each point.
A point passes when its median absolute difference is at most 5 percentage points and each block is within 10.
Do not repeat an unchanged run to obtain a pass. Keep the failed run as evidence.

See [the profile plan](https://github.com/TheAngryByrd/CozNet/blob/master/lode/plans/target-local-causal-profile.md) and [the API reference](../Reference/target-local-api.md).
