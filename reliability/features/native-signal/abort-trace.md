# Explicit abort trace boundaries

The trace records `run-abort` when the sampled workload catches an exception.
It associates that marker with the failure snapshot and stage.
Cleanup still precedes `run-end`.
The analyzer can correlate this explicit failed run without treating missing markers as valid.
The [evidence archive](data/abort-trace/run-20261014T042922592-f932bb40.zip) contains 102 files, verified against their retained originals.
Its SHA256 is `88108ceb03fd1be085471ef6fe69a9f25da73db18f1307f1fbdb6b5710aa1491`.
It separates timing capture, failure fixtures, and final analysis sources.

The accepted sequence contains an ordered prefix of the normal run markers, followed by `run-abort` and `run-end`.
The failed stage must match the last transition in that prefix.
The abort record must identify the failure snapshot and a failed protocol result.
The failure timestamp must follow the last prefix marker and precede the abort marker bracket.
Missing events, event loss, inconsistent alignment, and incomplete suspension episodes still fail correlation.
When no completed measurement exists, collection counters at the failure snapshot still require suspension evidence.

## Deterministic failure evidence

`SampledSignalAbortChecks` invokes the real workload with a clock that throws on its first read.
Its backend and sampler fixtures send no signals and collect no CPU samples.
The workload starts real target threads and uses its normal cleanup path.
The fixture requires a failed workload, matching abort metadata, ordered markers, joined targets, and completed restoration.
It returns success only when those expected failure conditions hold.

Both the cold and warm fixture pass on Windows.
The cold case fails inside the measurement stage.
The warm case fails inside warmup.
Linux run `run-20261014T043302262-65dd8063` captures both cases through EventPipe.
Both correlations pass with `workloadPassed=false`, `workloadProtocolPassed=false`, and `workloadAborted=true`.
The collector verifies unchanged sources and binaries across these cases.

```powershell
dotnet run --project build/build.fsproj -- -t LinuxAbortTraceVerify
```

The retained Linux fixture run invoked `pwsh -NoProfile -File native/VerifyLinuxAbortTraces.ps1` directly after building the workload.

## Timing capture

Run `run-20261014T042922592-f932bb40` uses one fixed block with target CPU capture.
It ran before the deterministic fixture was added.
The separate snapshots preserve that source difference.

```powershell
pwsh -NoProfile -File native/CollectLinuxRuntimeTraces.ps1 -TargetCpu -Repetitions 1 -EvidenceDirectory artifacts/abort-trace
```

| Case | Protocol | Capacity | Suspension correlation | Target CPU analysis |
| --- | --- | --- | --- | --- |
| Zero percent, no forced GC | Pass | Pass | Pass | No requests |
| Zero percent, forced GC | Pass | Pass | Pass | No requests |
| 50 percent, no forced GC | Pass | Pass | Pass | Fail: one inconsistent record |
| 50 percent, forced GC | Pass | Fail | Pass | Fail: three inconsistent records |

No timing case threw an exception, so none exercised `run-abort`.
The deterministic fixtures establish that path.
The GC treatment reaches 2.499886 ms of observed peer debt.
All four traces report zero lost events.
The treatment traces contain four and 101 complete suspension episodes, respectively.

Peer request 16608 remains pending at the GC treatment's peak debt.
It lasts 4.999320 ms and records no native wait.
It spends 4.996348 ms before the first handler clock reading.
Neither the request nor the peak service interval overlaps a recorded runtime suspension.
Its wider controller CPU interval records 5.004700 ms.
Its wider target CPU interval records 30,600 ns.
These raw observations do not override the failed target CPU analysis or establish a scheduling cause.

The earlier failed captures remain unchanged and retain their rejected correlations.
The new marker cannot repair a trace that did not record it.
Clock accuracy, general CLR safety, and physical prediction remain unverified.

See [runtime trace contracts](../../../lode/profiling/runtime-suspension-trace.md), [target CPU evidence](target-cpu.md), and [native clock measurement](target-cpu-clock.md).
