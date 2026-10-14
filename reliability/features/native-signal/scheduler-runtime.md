# Scheduler records with runtime suspension traces

The combined capture retains a forced-GC capacity failure with valid scheduler and runtime records.
Recorded runtime suspension intervals do not overlap every long request.
These observations do not establish the cause of the delay or justify debt forgiveness.

## Capture and analysis

`LinuxSchedulerTraceCollect` records one seeded four-case block through the existing EventPipe collector.
Each case also captures scheduler records for the controller and both targets.
The shared scheduler interface keeps Linux APIs outside shared accounting.
The public engine remains serial and synchronous.

```powershell
dotnet run --project build/build.fsproj -- -t LinuxSchedulerTraceCollect
python native/SchedulerRuntimeChecks.py
python native/AnalyzeSchedulerRuntime.py <decoded-run-directory>
```

The combined analyzer requires `workload.json` and decoded `events.json` in each case directory.
It runs both existing analyzers and joins request observations by `(slot, sequence)`.
It requires the aligned EventPipe marker process to match every scheduler capture process.
It rejects repeated request identities, incomplete runtime correlation, and scheduler capture errors.
Runtime overlap applies to the enclosing managed request.
It does not measure overlap within the native phase or the observed switched-out interval.

Seven deterministic checks pass on Windows without native APIs.
They verify identity matching after scheduler sorting, duplicate rejection, process mismatch rejection, incomplete correlation, scheduler loss, and retained capacity failure.
The command-line check preserves valid cases when another case fails analysis.
Valid analysis does not imply a passing workload.

## Retained evidence

The [archive](data/scheduler-traces/run-20261014T052017650-9883e1f3.zip) contains raw traces, workload records, capture snapshots, analysis snapshots, and runtime metadata.
All 73 archived files match their retained originals by SHA256.
The archive SHA256 is `e03486e6ff931bc40a0c9078785b2e83deb4b859f85109b41addf015eee3ebf7`.
The Linux kernel is `5.15.153.1-microsoft-standard-WSL2`.
The order uses seed `20261019`.
The collector exits with failure after retaining the forced-GC treatment failure.
Source and binary verification passes for the capture snapshot.
Later analysis files remain separate from that snapshot.

Review exposed missing process identity checks in the initial combined analyzer.
The [final check archive](data/scheduler-traces/checks-20261014T053600.zip) preserves the corrected analyzers, 45 passing Python checks, and reanalysis of all four retained cases.
All 16 archived files match their retained originals by SHA256.
The archive SHA256 is `c1460151d33b712943e75346b04d1eb1e3ba31d6b199640111b80fa5c3e14059`.
Reanalysis passes without changing the recorded workload outcomes or overlap values.

| Case | Workload result | Complete runtime episodes | Alignment width, ns |
| --- | --- | ---: | ---: |
| GC, 0% | Pass | 110 | 5,739 |
| GC, 50% | Capacity failure | 106 | 8,767 |
| No GC, 0% | Pass | 1 | 9,702 |
| No GC, 50% | Pass | 5 | 9,573 |

All four cases have valid runtime correlation and zero reported EventPipe event loss.
All scheduler streams pass integrity checks with zero final reported loss.
The GC treatment reaches 39,627,278 ns of peer debt.
It fails the unchanged 2,000,000 ns debt limit while passing protocol checks.
Request 3649 remains pending at peak debt.
The peak service interval has no runtime suspension overlap.

## Request observations

Both requests below belong to peer slot 1 and perform zero native wait.

| Sequence | Before first handler clock, ns | Observed target switched-out overlap, ns | Enclosing request suspension overlap |
| --- | ---: | ---: | --- |
| 3641 | 15,319,636 | 15,314,832 | One envelope, 1,000,619 ns guaranteed and 1,018,153 ns possible |
| 3643 | 10,692,644 | 10,686,898 | None |

After request 3641's last handler clock reading, the controller has 22,987,986 ns of observed switched-out time within a 23,099,081 ns interval.
The first and last handler clock readings do not mark exact handler entry and completion.
The switch-out record for request 3643 lacks the preemption flag.
That flag does not identify a blocking cause or a wakeup time.
The absence of suspension overlap does not exclude indirect effects from earlier runtime activity.

## Runtime provenance and restart semantics

The retained image ID is `sha256:3db72bd242b5f26a45d74c2d49a7654740e75c732654658177e52aa498645c14`.
It contains `Microsoft.NETCore.App 10.0.0`.
The runtime `.version` reports product commit `b0f34d51fccc69fd334253924abd8d6853fad7aa`.
The `libcoreclr.so` SHA256 is `17daf78494a7b48239c1ef4985daaef3a0ce8fa5fe84dd8fefa49cf83bb0ea22`.
The [product source manifest](https://github.com/dotnet/dotnet/blob/b0f34d51fccc69fd334253924abd8d6853fad7aa/src/source-manifest.json) maps runtime sources to `60629d14374c56f1cb51819049ad1fa529307f8d`.
The initial metadata query failed because the read-only image had no writable temporary directory.
The successful query adds a temporary filesystem at `/tmp`. Both outputs remain in the archive.

[`RestartEE`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/threadsuspend.cpp#L5380-L5495) clears GC state and invokes `ResumeAllThreads` before emitting `GCRestartEEEnd`.
[`ResumeAllThreads`](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/threadsuspend.cpp#L3588-L3638) releases runtime barriers and signals the GC completion event.
It does not wait for each worker to execute.
Each [worker continuation](https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/threadsuspend.cpp#L2215-L2237) returns from its wait separately.
Thus, `GCRestartEEEnd` cannot establish a target service deadline or justify clearing debt.

## Design consequence

Target-side settlement can reduce controller round trips. It cannot eliminate the observed target off-CPU intervals.
A service change must preserve request ownership, matched acknowledgments, measured waits, and the existing failure limits.
Do not treat missing service as an applied speedup.
Physical-prediction accuracy and general CLR signal safety remain unverified.

See [scheduler capture](scheduler-capture.md), [scheduler cost](scheduler-cost.md), and [runtime trace contracts](../../../lode/profiling/runtime-suspension-trace.md).
