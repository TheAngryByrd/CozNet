# How to check candidate discovery on Linux

Use this procedure to check that candidate discovery ranks live samples of a zero-delay run on the Linux backend.
It requires the .NET 10 SDK, PowerShell 7, Python 3, Docker, and the images `coznet-native-build:10.0.100` and `mcr.microsoft.com/dotnet/sdk:10.0.100`.
The Docker engine must run Linux x64 containers. The NuGet package folder on the host must hold all packages of `tests/CozNet.Tests`, because the Linux test step has no network access.

1. Commit or remove all changes. A changed file does not stop the check, but the manifest then records `treeClean` as false.
2. Make sure that no other target-local runner runs.
3. Run this command from the repository root:

   ```sh
   dotnet run --project build/build.fsproj -- -t TargetLocalDiscoveryVerify
   ```

   The runner runs the discovery tests on Linux first. When they do not pass with an executed count above 0, it stops before the capture.
4. Open the new directory under `artifacts/target-local-discovery`.
5. Open `summary.json`.
   - `contract` names the evidence contract that the analyzer applied, and `selection` says how it chose it.
   - `assessment` is `passed`, `failed`, or `unsupported`. It is `passed` only when the run is valid and every criterion of the contract holds.
   - `reasons` lists each criterion that failed. `notEvaluated` lists the checks that the contract does not apply.
   - `workflowEvaluated` is true only for the current contract. A legacy result never validates the library workflow.
   - `treeClean` and `commit` give the state of the tree at the start.
6. Open `discovery.json` to see the process id, the hashes, the sample classes, the ranked methods, the source anchors, and the design result. `discovery.json.perf-map` holds the perf map that the process read.

To analyze a retained run again without a change to its folder, write the summary elsewhere:

```sh
python native/AnalyzeTargetDiscovery.py <run folder> --output <file outside the run folder>
```

Add `--contract legacy-1` only for an unversioned record that you know comes from the harness at `a0f650f`.
The analyzer exits with 0 for a current pass, 3 for a legacy pass, and 1 for a failure or an unsupported record.

A check that does not pass makes the command fail. Do not run the check again to get a pass. Keep each run as evidence.
A passed check shows that discovery works on live samples in this workload. It does not qualify prediction accuracy.

See [the API reference](../Reference/target-local-api.md#candidate-discovery) and [the discovery limits](../Explanations/target-local-discovery.md).
