# Matched physical worker evidence

The shared worker implementation serves sampled-timer and physical runs.
Both paths use two dedicated threads, the same release gate, checksum validation, and separate progress publication.
The physical controller sleeps through the measurement window and loads no native timer adapter.

## Portable checks

The Release build and `--verify-timer-layout` passed on Windows x64 and Linux x64.
The Linux executable ran in `mcr.microsoft.com/dotnet/sdk:10.0.100` without native timer libraries.
The verifier includes these new checks:

| Check | Contract |
| --- | --- |
| `InvalidModeIsRejected` | Baseline and optimized modes preserve selection. Other names fail parsing. |
| `StopBeforeReleaseProducesNoProgress` | Stop opens the gate and prevents either worker from executing a batch. |
| `BothWorkersPublishProgress` | Both modes retain zero progress before release, publish separate progress, and join without errors. |

The verifier emits no case counts. These are source-registered checks.
Logs and raw physical results are under `artifacts/matched-physical/`.
Physical source and result hashes were recorded after execution. No pre-execution physical manifest exists.
The verifier logs retain exit codes without source fingerprints.

## Physical observations

Both methods ran once before worker creation. Tiering and ReadyToRun were disabled.
Each worker checked `49995000` before publishing a completed batch.
All four runs returned zero, completed their boundary captures, and joined both workers.

| Platform and mode | Nominal elapsed seconds | Frozen selected batches | Frozen peer batches |
| --- | ---: | ---: | ---: |
| Windows baseline | 2.012034600 | 629400 | 627703 |
| Windows optimized | 2.006764900 | 1236188 | 615449 |
| Linux baseline | 2.000285140 | 623648 | 625510 |
| Linux optimized | 2.000235230 | 1240128 | 624021 |

The nominal window is two seconds. Scheduler wakeup can extend the observed duration.
These four observations establish execution and retained endpoints, not a calibrated optimization or a prediction series.
They were not randomized comparison blocks.

## Timer regression observation

`LinuxFastTimerCapacity` retained run `20261015T002421204-b7897b3f`.
Its manifest reports unchanged inputs.
Layout and zero-percent cases passed.
The treatment failed at 2,499,910 ns of peer debt under the unchanged 2,000,000 ns gate.
Its capture completed and native cleanup closed the session.
The failed treatment remains retained. No retry was used to obtain a passing result.

## Limits

Physical calibration, attribution of pending sample periods and native journal suffixes, and prediction analysis remain unimplemented.
The shared loop does not establish general application support or CLR safety.
No accuracy claim follows from these results.
See [the reference](../../../docsSrc/Reference/matched-physical.md) and [the boundary contract](../../../docsSrc/Reference/measurement-boundaries.md).
