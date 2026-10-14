# Native owner scheduling

Run: `artifacts/linux-sampled-service/20261014T173407802-f76884e2`.
Command: `dotnet run --project build/build.fsproj -- -t LinuxServiceSchedulerTiming`.

This capture adds the native owner thread to the preceding three-thread diagnostic.
The native ABI returns its Linux thread ID after the owner installation handshake.
The analyzer requires the fourth stream to match that ID.
All scheduler captures stop before target retirement and native owner restoration.

Native ABI, accounting, scheduler, initialization, Python, and zero-speedup checks passed.
Both scheduler analyses passed their integrity checks.
The normal and failed-owner-stop fixtures passed on Windows and Linux.
Eight focused Python checks passed, including owner identity, missing-stream, and loss rejection.

The treatment failed during warmup at 22.0395 ms with 2,999,973 ns peer debt.
The 2,000,000 ns limit remains unchanged.
Targets joined and the session restored.
`summary.json` reports unchanged inputs and an overall failure.
The source snapshots, all raw records, analyses, and failures remain retained.

## Streams

| Thread | Role | Records | Final lost records |
| --- | --- | ---: | ---: |
| 7 | Managed controller | 664 | 0 |
| 17 | Selected target | 20 | 0 |
| 18 | Peer target | 6 | 0 |
| 14 | Native owner | 1,014 | 0 |

## Observation gap

Command 19, child 8, has the longest interval from the last handler clock reading to native observation.
Its duration is 6,239,753 ns.
The owner has one observed switch-out interval from 347142133431994 ns to 347142139678167 ns.
The record marks the switch-out as preemption.
Its overlap with the observation gap is 6,236,648 ns, leaving 3,105 ns outside that overlap.
The controller and peer target have no observed switch-out overlap during the gap.

The child's measured handler interval is 50,164 ns.
The interval from request start to the first handler clock reading is 2,013 ns.
The clock readings exclude the handler prefix before the first reading and the publication suffix after the last reading.
Neither interval has observed target switch-out overlap.
Thus, the retained long interval starts after the measured wait, while the native owner is predominantly switched out.
This observation establishes an owner scheduling gap in this run.
It does not identify why the scheduler preempted the owner or explain every earlier failure.

The owner must observe the previous acknowledgment before it can issue another bounded child.
Its preemption therefore interrupts continued service even while the managed controller and target continue executing.
Off-CPU time does not forgive intervention debt.
Increasing thread priority or weakening limits is not validated by this capture.
The next design review must compare this dependency with upstream Coz's per-thread debt payment.

See [the three-thread capture](scheduler.md) and [the owner contract](../../../../lode/profiling/native-service-owner.md).
