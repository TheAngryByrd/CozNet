# Peer preparation cadence

The controller now skips peer preparation when its immutable accounting state has not changed.
It still polls native acknowledgments on every iteration.
The seven accounting replay checks pass on Windows and Linux.
The live Linux run fails. Lower allocation does not establish stable capacity.

## Contract

`PeerSignalCadence` retains the resulting state after preparation and request submission.
New sample observations and matched completions create a different accounting state.
They permit another peer check, including a retry after a deferred completion.
A preparation skipped for a pending request cannot suppress the later completion transition.
Settlement does not use the cadence cache. Measurement starts with a fresh cache after warmup.

The checks cover initial service, unchanged state, new credit, partial settlement, deferred completion, credit during a pending request, fresh accounting, and zero-credit observations.
The unchanged-state check observes zero allocation across 10,000 calls.
These checks use the real accounting model. They do not prove native scheduling behavior.

## Retained measurement

Command: `dotnet run --project build/build.fsproj -- -t LinuxWarmSampleCadenceVerify`.
The [archive](data/peer-cadence/run-20261014T021847556-0e27afaf.zip) retains all six process outcomes and four contract-check outcomes.
The run uses three fixed zero-then-50-percent blocks, 250 ms warmup, and two-second measurement windows.
It uses the same bounds as the [previous ring-cache run](gc-and-ring-cache.md).
This is a sequential comparison, not a randomized comparison.

| Case | Previous allocation (MiB) | Current allocation (MiB) | Current GC pause total (ms) | Current result |
| --- | ---: | ---: | ---: | --- |
| Block 1, zero percent | 2848.097 | 447.293 | 17.768 | Pass |
| Block 2, zero percent | 2880.186 | 498.657 | 9.190 | Pass |
| Block 3, zero percent | 2854.477 | 498.579 | 8.746 | Pass |
| Block 1, 50 percent | 1539.258 | Unavailable | Unavailable | Warmup protocol failure |
| Block 2, 50 percent | 1549.269 | 381.133 | 19.610 | Debt limit failure |
| Block 3, 50 percent | 1526.571 | 375.341 | 18.846 | Debt limit failure |

Allocation measures the controller thread. GC pause totals cover the process.
The counter window includes sample enablement and boundary diagnostics.
The incomplete first 50-percent case has no measurement allocation value.
Its warmup failure boundary records 555,168 allocated bytes and no GC pause increase.

The completed zero-percent cases perform 1,784, 1,979, and 1,962 peer checks.
They skip 19,330,402, 21,560,781, and 21,558,997 unchanged-state checks.
The completed 50-percent cases perform 10,614 and 10,539 peer checks.
They skip 15,543,417 and 15,291,184 unchanged-state checks.
These counters exclude preparation attempts that return early for a pending request.

## Failures

The first 50-percent case fails during warmup.
Request 1 waits 6.839216 ms and records a 6.840000 ms handler interval.
This exceeds the unchanged 5 ms handler limit for a requested 50 microsecond wait.
The process retains its failed request, joins its workers, and restores the backend.

The second and third 50-percent cases complete their protocol checks.
Their peak peer debts are 2.374890 ms and 18.381352 ms.
Both exceed the unchanged 2 ms observed debt limit.
Request 9363 remains pending at the third block's peer debt peak.
It records 3.446003 ms before the first handler clock reading and a 0.078708 ms handler interval.
This observation does not explain all accumulated debt or identify its cause.

## Evidence limits and next step

`AnalyzeTiming.ps1` verifies 24 source and binary hashes and reconciles credits and waits across ten completed phases.
It marks the failed warmup process incomplete, with zero fully verified phases.
`AnalyzeCounters.ps1` checks ordered reads and monotonic counters in eleven phase windows, including the partial warmup.
The archive contains both scripts, their results, raw records, environment, logs, and outcomes.
The failed workload was not rerun to replace its result.

Allocation remains substantial. Measure per-iteration accounting reads before changing accounting semantics.
Keep wait overshoot and delivery delays as separate unresolved problems.
Forced-GC sampled runs, matched controls, and physical-change prediction remain incomplete.
The two-percentage-point prediction gate remains unchanged.
