# Target-local throughput progress points

## Problem

The causal profile used the end of the workload as its only progress point. Coz also supports throughput progress points.
Each visit of a throughput point marks one unit of work, and Coz reports the change in the effective time for each visit.

## Decision

A throughput experiment runs for a fixed wall time and counts visits. No native experiment switch is needed, because each experiment is still one bound run.
- `TargetProgressPoint` is `EndToEnd` or `Throughput of durationMs`. `TargetCausalDesign.tryCreate` takes it and rejects a duration outside 1 to 60000 ms with `InvalidDuration`.
- The driver gives each experiment a new `TargetProgressCounter`. Workers call `Visit` for each unit of work and stop when `Stopped` is true.
- For `Throughput`, the driver monitor waits until a worker works, sleeps for the duration, and then calls `Stop`. Work in progress at the stop finishes and counts.
  The monitor also calls `Stop` when it fails. A watcher thread calls `Stop` 1 s plus ten times the duration after the experiment starts, for a run that never reaches its monitor.
  An experiment that the watcher stops does not pass, because its work interval is not the fixed duration.
- Each experiment records `Visits`. For `EndToEnd`, `Visits` is 1.
- The period of an experiment is its effective duration divided by its visits. The prediction is `1 - P / P0`, with P0 the period of the baseline of the same block.
  An experiment or baseline with no visits gives no prediction. Its experiment record stays.
- The effective duration counts all time from the first work start to the last finish, minus the global delay. All visits in that time count, so the time and the visits match.

## Qualification

The sample mode `throughput-profile` runs one block for 120 ms in each experiment. The selected worker repeats `Work.selected 40` and the peer repeats `Work.peer 20`, with one visit after each call.
The candidates and speedups are the same as in the causal profile: `Work.selected` and `Work.peer` at 25, 50, and 75.
The sample mode `plain-throughput` runs the same loops without the profiler for 120 ms. An optimized arm reduces the rounds of the candidate: 30, 20, and 10 for the selected worker, or 15, 10, and 5 for the peer.
The actual gain of a point is `1 - (D / N) / (D0 / N0)`, where D is the measured run time and N the visits of the optimized arm, and D0 and N0 are those of the original arm in the same block.

Both workers update one counter. Contention on it is part of the measured period, and the plain arms use the same counter.
The limit of 100 visits is one fifth of the visits of the original arm in a development run, so a slower host still has a margin.
In the development run, the `Work.peer` experiment at 75% broke the 2 ms debt bound. The selected worker then pays three quarters of the time of the peer. A qualification run can fail at that point, and the run then stays as evidence.

If both workers have the same cost for each round, the expected gains are 10%, 25%, and 50% for `Work.selected` and 18.2%, 40%, and 66.7% for `Work.peer`.
The criteria compare predictions with the measured actual gains, not with these expected values.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 41 and change none.

42. Each throughput profile record holds the fixed design of criterion 38, with mode `throughput-profile`, its seed, a 120 ms duration, and at least 100 visits in each passed experiment.
    Each passed experiment has `EffectiveNs` equal to `ElapsedNs` minus `GlobalNs`. Each prediction that the driver writes equals the prediction that the analyzer computes.
43. Each plain throughput record has mode `plain-throughput`, the rounds of its arm, a 120 ms duration, and at least 100 visits. Each latency profile experiment has one visit.
44. Each of the six points has 5 valid blocks. Its median absolute difference is at most 5 percentage points, and each block is within 10.
45. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local causal profile](target-local-causal-profile.md) and [target-local status](target-local-status.md).
