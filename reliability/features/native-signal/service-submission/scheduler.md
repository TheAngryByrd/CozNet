# Native service scheduler capture

Run: `artifacts/linux-sampled-service/20261014T172713495-1bef681e`.
Command: `dotnet run --project build/build.fsproj -- -t LinuxServiceSchedulerTiming`.

The runner retains source and binary hashes, copied analyzers, checks, workload records, and scheduler analysis.
`summary.json` reports unchanged inputs and an overall failure.
No unchanged timing case was repeated.

Native, accounting, scheduler, initialization, Python, and zero-speedup checks passed.
Both scheduler analyses passed their integrity checks.
The 50-percent workload failed during measurement at 398.1435 ms with 2,049,444 ns of peer debt.
The debt limit remains 2,000,000 ns. Targets joined and the session restored.

## Captured streams

| Thread | Role | Records | Final lost records |
| --- | --- | ---: | ---: |
| 7 | Managed controller | 34,866 | 0 |
| 16 | Selected target | 496 | 0 |
| 17 | Peer target | 250 | 0 |

The analyzer matched 40 boundary prefixes with no unmatched prefix.
It found no delayed snapshot publication of the kind found in the earlier capture.
This run does not explain that earlier event.

Command 216 has the longest first managed receipt interval, during warmup.
Its eighth child spans 1,537,401 ns from native observation to the first managed receipt upper bound.
Observed controller switch-out pairs overlap 626,305 ns of that interval.
The overlap does not account for the entire interval or establish a blocking cause.

## Native child phases

The extended analyzer reuses this retained workload in `scheduler-phases-50.json`.
Its source, checks, input, and output hashes are recorded separately in `scheduler-phases-manifest.json`.
At analysis time, seven focused checks passed, including native phase overlap and malformed capture rejection.
The original copied analyzer and its output remain unchanged.

Command 742 is the final live command before the capacity failure.
Its third child has the longest interval from the last handler clock reading to native observation: 2,413,347 ns.
The controller is observably switched out for 96,694 ns of that interval.
The peer target is observably switched out for 30,061 ns.
The measured handler interval lasts 50,244 ns with no observed target switch-out overlap.
The time before the first handler clock reading is 1,949 ns with no observed target switch-out overlap.
These clock readings exclude the handler prefix and publication suffix.

The native owner reads acknowledgments. This capture does not include that owner's thread.
Thus, neither controller nor target switch records establish the owner's scheduling or mutex acquisition delay.
The next diagnostic must capture the native owner thread before it can attribute the observation gap.
Off-CPU observations do not clear debt or establish physical prediction accuracy.

See [the preceding submission capture](README.md) and [scheduler contracts](../../../../lode/profiling/scheduler-records.md).
