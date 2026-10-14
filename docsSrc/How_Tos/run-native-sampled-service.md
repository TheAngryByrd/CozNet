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

To capture native submission intervals, run the `LinuxServiceSubmissionTiming` target.
This mode includes managed and native transport initialization.
Inspect `Submission` and `SubmissionTiming` in terminal and pending service journals.
Keep the initialization journals separate from live measurements.
Use the [timing reference](../Reference/native-service-accounting.md#native-submission-timing) for units and capture boundaries.
The [first submission capture](../../reliability/features/native-signal/service-submission/README.md) fails during measurement at the unchanged debt limit.

To check retained boundary snapshots for delayed publication, run:

```sh
python native/AnalyzeServiceSnapshotLag.py path/to/speedup-50.json path/to/snapshot-lag-50.json
```

Inspect `matchedPrefixes`, `unmatchedPrefixes`, and `findings` in the output.
An unmatched prefix has no later journal for comparison. It does not prove the absence of delay.
The analyzer rejects inconsistent identities, counts, clocks, and child prefixes.
Each finding reports a lower bound after the owner snapshot copy, using native timestamps only.
The output preserves workload failure. It does not identify a scheduling cause.

To include controller, native owner, and target switch records, run `LinuxServiceSchedulerTiming`.
This target enables initialization, submission timing, and the existing Linux scheduler backend.
Inspect `scheduler-analysis-0.json` and `scheduler-analysis-50.json` in the retained run directory.
The analyzer requires complete loss accounting and stopped captures from four distinct threads in one process.
It matches the fourth stream to the native owner thread ID returned by the backend.
It compares native snapshot bounds and receipt intervals with observed controller switch-out intervals.
Incomplete switch pairs remain censored. Overlap does not establish the reason for a switch-out.
The runner retains failed workloads and runs analysis before it reports the final result.
`longestBeforeHandler` and `longestAfterHandler` compare native child phases with target and controller switch records.
`ownerAfterHandler` and `ownerBeforeHandler` expose native owner switch-out overlaps.
`BeforeHandler` ends at the first handler clock reading. `AfterHandler` starts at the last handler clock reading.
`DuringHandler` covers the measured interval between those readings. It excludes the handler prefix and publication suffix.
Older three-thread records remain readable but cannot establish owner scheduling.
See the [earlier scheduler comparison](../../reliability/features/native-signal/service-submission/scheduler.md) for that limitation in a failed treatment.

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

## Compare native owner polling

1. Run `dotnet run --project build/build.fsproj -- -t LinuxServiceOwnerPolling` from the repository root.
2. Open the retained run directory under `artifacts/linux-sampled-service`.
3. Compare `yield-speedup-0.json` with `poll-64-speedup-0.json` for CPU cost.
4. Inspect both `*-speedup-50.json` files for failures and peak debt.
5. Inspect each `*-scheduler-analysis-*.json` for capture validity and owner switch-out overlap.

The target runs each policy once at zero and 50 percent. It runs yield first.
It uses managed and native initialization, submission timing, and four scheduler streams.
All existing timing limits remain active. Failed cases remain in the result directory.
The target reports failure after it retains all case outcomes and analyses.
Do not infer a causal improvement from one fixed-order comparison.
See [the polling contract](../Reference/native-service-accounting.md#portable-owner-polling-policy)
and [the scheduling explanation](../Research/native-owner-scheduling.md).
