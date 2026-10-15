# Counter history cost

The immutable transition journal removes repeated history copies from snapshot creation and direct-parent session admission.
It preserves full prefix comparison for histories without a shared parent.
This report separates component measurements from live profiler behavior.

## Component protocol

The scratch driver calls the compiled workload's `CpuCounterJournal.Snapshot` and `BoundCpuSession.Enable` methods through compiled delegates.
It constructs legal histories through the existing session parser before measurement.
Source operations do no native work, and the counter reader returns zero.
Each advance specimen accepts one fixed valid child snapshot from a fixed disabled session.
The control delegate returns that existing snapshot.

Eight shuffled blocks use seed `20261015`.
Each block measures 200 calls per stage at 2, 200, 2,000, and 20,000 prior transitions.
The driver retains all 96 rows, elapsed timestamp brackets, current-thread allocation counts, runtime identity, and input hashes.
Setup and warmup remain outside the recorded brackets.
The native transition operation and creation of the new immutable node are not measured by these stages.

Both runs use Linux x64, .NET 10.0.0, and the same SDK container image.
Tiered compilation and ReadyToRun are disabled.
The driver source and probe binary remain the same between runs.

| Implementation | Retained directory |
| --- | --- |
| Full history copies | `artifacts/counter-history-cost/20261015T173304733` |
| Immutable history | `artifacts/counter-history-cost/20261015T174318135` |

Both processes exited zero and retained unchanged inputs.

## Component observations

| Prior transitions | Old mean advance time | New mean advance time | New allocation per advance |
| ---: | ---: | ---: | ---: |
| 2 | 0.234 microseconds | 2.900 microseconds | 176 bytes |
| 200 | 9.243 microseconds | 0.072 microseconds | 176 bytes |
| 2,000 | 86.317 microseconds | 0.067 microseconds | 176 bytes |
| 20,000 | 944.365 microseconds | 0.087 microseconds | 176 bytes |

The new two-transition mean includes one 200-call bracket of 4.545060 ms.
That bracket remains in the mean. Its cause was not captured.
These results do not establish uniformly lower timings or a latency bound.

New snapshot allocation is 144 bytes per call at every measured history size.
New advance allocation is 176 bytes per call at every measured history size.
The old 20,000-transition stages each allocate about 960 KB per call.
The control delegates allocate zero bytes.
The measurements support removal of history-dependent copying on the shared-history path.
They do not measure arbitrary-list fallback, final enumeration, serialization, or total profiler overhead.

## Contract checks

The portable counter checks retain failure records, clock validation, and capacity rejection before source effects.
The portable session checks retain rejection of changed prefixes and mutable input isolation.
New checks verify shared snapshot identity, old-snapshot stability, equivalent independent histories, and changed immutable branches.
They also reject an invalid new transition on valid ancestry and preserve chronological JSON array output.
The closure check rejects shorter, longer, reordered, and changed histories with otherwise unchanged final evidence.

The Windows host counter and pausable managed groups pass.
The Linux counter checks also pass with the same workload binary as the live comparison.

## Live comparison

Run `artifacts/bound-selected-cadence/20261015T174523653-51d963c6` uses the existing continuous/qualified/qualified/continuous order and 5,000 selected batches.

| Order and mode | Treatment result | Selected work duration | Pauses | Mapped complete intervals |
| --- | --- | ---: | ---: | ---: |
| 0: continuous | Pass | 4,788.454 ms | 23,361 | 1 |
| 1: qualified | Pass | 205.576 ms | 38 | 16 |
| 2: qualified | Pass | 179.246 ms | 35 | 13 |
| 3: continuous | Fail after 681 batches | No completed endpoint | 388 | 2 |

The last treatment retains 3,672,285 ns of selected debt against the unchanged 2,000,000 ns limit.
Both owners and the controller joined, native ownership closed, and cleanup reported no errors.
The overall comparison remains failed.
All zero-speedup and invalidation cases passed.
All twelve records passed evidence replay, which preserved the failed treatment.
Every child retained unchanged source and binary hashes and passing native, managed, outcome, and Python checks.

The continuous pass still earns credit from only one complete selected interval.
The reduced copying does not correct this coverage problem.
The before/after captures are separate sequences, with different pause counts and peer progress.
They do not isolate the journal's contribution to elapsed time or establish stable capacity and prediction accuracy.

See [the session contract](../../../docsSrc/Reference/bound-cpu-session.md),
[counter evidence](../../../docsSrc/Reference/cpu-counter-evidence.md), and
[the fixed-work procedure](../../../docsSrc/How_Tos/run-bound-selected-cadence.md).
