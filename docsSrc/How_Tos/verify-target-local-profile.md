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

Each block runs one profile process and seven plain processes in a random order.
The profile process runs a baseline and six experiments: `Work.selected` and `Work.peer` at speedups 25, 50, and 75.
The plain processes run the original workload and one optimized workload for each point.
A point passes when its median absolute difference is at most 5 percentage points and each block is within 10.
Do not repeat an unchanged run to obtain a pass. Keep the failed run as evidence.

See [the profile plan](https://github.com/TheAngryByrd/CozNet/blob/master/lode/plans/target-local-causal-profile.md) and [the API reference](../Reference/target-local-api.md).
