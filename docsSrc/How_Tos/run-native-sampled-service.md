# How to run the native sampled service experiment

Use this procedure to inspect native service behavior under sampled load.
The experiment is an internal Linux x64 prototype.
Its first retained treatment fails during warmup. Capacity and prediction accuracy remain unverified.

1. Install the .NET 10 SDK and PowerShell 7 on the host.
2. Start a Linux Docker engine.
3. Make these images available locally:

   - `coznet-native-build:10.0.100`
   - `mcr.microsoft.com/dotnet/sdk:10.0.100`

4. From the repository root, run:

   ```sh
   dotnet run --project build/build.fsproj -- -t LinuxSampledServiceVerify
   ```

   The target builds the managed workload and both native libraries.
   It runs native checks, managed accounting checks, and separate zero-speedup and 50-percent processes.
   Each workload uses spin waits, qualified-sample cadence, 250 ms warmup, and a two-second measurement window.
   The process watchdog is 30 seconds. This runner does not force GC.

5. Open the new directory under `artifacts/linux-sampled-service/`.
6. Inspect `summary.json`, `outcomes.json`, and `manifest.json`.
7. Inspect each `speedup-*.json` file, including `failures`, `failureBoundary`, `serviceJournals`, and `pendingServiceJournals`.

   A preparation failure can precede the next peak-debt update.
   In that case, `failureBoundary` contains the observed debt that caused rejection.

8. Retain every result before investigating a failure.

See the [first retained run](../../reliability/features/native-signal/sampled-service/README.md) and [accounting limits](../Reference/native-service-accounting.md).

## Inspect controller startup phases

To record startup phases, run `LinuxSampledServicePhases` instead of `LinuxSampledServiceVerify`.
Inspect `controllerPhases` in each result.
The diagnostic retains the first 128 drain, completion, and preparation phases.
Each record contains bracketed thread CPU readings, process timestamps, and a completion flag.
An incomplete phase identifies the operation active when the workload failed.

Compare the CPU difference with the elapsed interval between the readings.
CPU work cannot identify JIT or parsing costs without further evidence.
A large elapsed interval with little CPU work can include descheduling or blocking.
The diagnostic adds clock reads and allocations. Do not treat it as an overhead-free capacity baseline.

## Correlate startup with runtime compilation

1. Install the trace collector from the repository root:

   ```sh
   dotnet tool install dotnet-trace --version 10.0.750501 --tool-path lode/tmp/diagnostic-tools --allow-roll-forward
   ```

2. Run the `LinuxSampledServiceJit` build target.
3. Decode each retained `speedup-*.nettrace` with `native/ExportRuntimeTrace.ps1` and its `-IncludeJit` switch.
4. Match the decoded clock markers to `traceAnchors` before comparing runtime events with controller phases.
5. Reject a correlation when the trace reports lost events or inconsistent clock brackets.

Use `native/AnalyzeServiceJit.py` with the workload JSON, decoded events JSON, and output path as its three arguments.
The analyzer checks clock alignment and merges nested compilation intervals before computing phase overlaps.
See [the retained startup analysis](../../reliability/features/native-signal/service-startup/README.md) for an executed example.

This diagnostic enables JIT and GC events. Trace collection adds overhead and cannot establish untraced capacity.

## Compare managed initialization

Run `LinuxInitializedServiceVerify` to exercise managed controller paths before the native backend starts.
The target first checks initialization isolation at zero and 50 percent.
It then runs the same live cases with phase capture and unchanged timing limits.

Inspect the separate `initialization` object in each workload result.
It contains one synthetic perf sample and two synthetic service journals.
These records do not represent native delivery or application work.
The live workload starts with fresh accounting and an empty sample list.
Inspect `transportInitialization` for the native setup request.
Both workers remain at their start gate during that request.
It uses synthetic credit and must return one deferred child with zero measured wait.
The live experiment then starts with fresh accounting. Native command numbering continues across setup and measurement.
Neither initialization record is evidence of measured intervention in eligible application code.
