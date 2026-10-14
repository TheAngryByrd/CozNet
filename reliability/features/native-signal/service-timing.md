# Sampled signal service timing

The timing experiment identifies both wait overshoot and delayed acknowledgment during capacity failures.
A delayed acknowledgment also occurs for a request that performs no native wait.
The effect of another sleep method remains untested.
The experiment does not establish stable capacity, prediction accuracy, or general CLR safety.

## Method

```powershell
dotnet run --project build/build.fsproj -- -t LinuxWarmSampleCadenceVerify
```

The runner uses three fixed blocks. Each block runs zero percent before 50 percent in separate processes.
Each process warms the same two native workers and controller for 250 ms before a two-second measurement.
The limits remain 50 µs requested wait, 5 ms handler interval, 100 ms acknowledgment, and 2 ms observed debt.
The two-percentage-point prediction gate remains unchanged. This experiment does not test prediction accuracy.

The [raw archive](data/sampled/run-20261014T012404512-e018183d.zip) contains all six cases, runner outcomes, source/binary hashes, and timing verification.
All six cases pass protocol checks. All three zero-percent cases pass capacity checks.
The 50-percent cases in blocks 1 and 2 fail capacity. The target therefore fails overall.

| 50-percent block | Credit (ms) | Peak selected debt (ms) | Peak peer debt (ms) | Longest service interval (ms) | Capacity |
| --- | ---: | ---: | ---: | ---: | --- |
| 1 | 915.5 | 3.009993 | 1.870806 | 3.225932 | Fail |
| 2 | 901 | 0.929264 | 3.499801 | 3.694768 | Fail |
| 3 | 912.5 | 0.850247 | 1.444821 | 1.658160 | Pass |

The zero-percent longest service intervals are 1.287392, 0.551855, and 1.547356 ms.
The longest interval is not necessarily the interval that produces peak debt.

## Block 1: peer wait precedes selected debt

Peer request 8452 asks for 50 µs and records 3.743108 ms of actual wait.
Its measured handler interval is 3.743613 ms. Its observed acknowledgment takes 4.4971 ms.
The selected peak follows acknowledgment 8452 in the same service iteration.
At that snapshot, global delay is 800.194987 ms.
Selected credit plus selected wait is 797.184994 ms. The difference is 3.009993 ms of selected debt.
The peak's service interval lasts 3.147015 ms and consumes no samples.

The accounting publishes the peer's measured overshoot as global delay.
These records connect the selected debt peak to the peer wait.
They do not identify why the operating system extended that wait.

## Block 2: a pending request performs no wait

Peer request 9034 remains pending for 6.980643 ms at the peer peak.
The peer owes 3.499801 ms at that snapshot.
The associated service interval lasts only 0.008517 ms and still finds the request pending.
The controller therefore observes the pending request near the peak, rather than only after a seven-millisecond polling gap.
The controller consumes seven observations between request publication and the snapshot. They contribute 3 ms of selected credit.

The controller observes the acknowledgment 0.015186 ms after the snapshot.
The result is deferred because the interrupted address is outside the permitted method range.
It records zero wait and a 625 ns measured handler interval.
The observed acknowledgment duration is 6.9958 ms.

The requested sleep does not execute in this request.
This request contains no measured sleep overshoot. Earlier waits can still affect backlog and scheduling.
The records do not separate time before handler entry from time after handler completion and before controller observation.
Native handler entry and completion timestamps are the next measurement needed to separate those intervals.

## Clock and verification limits

`StartedTimestamp`, `CompletedTimestamp`, `ConsumedTimestamp`, and boundary `Timestamp` use the process `Stopwatch` clock.
This run records a frequency of 1,000,000,000 ticks per second.
Divide a timestamp difference by that frequency to obtain seconds.
`AcknowledgmentMilliseconds` uses `TimeSpan` precision and can differ from the raw difference by less than 100 ns.
Perf sample timestamps have a separate clock contract. This report does not equate those clocks.

Service intervals run between successive completed controller iterations. The first interval starts before worker release.
An interval includes diagnostics from the preceding iteration. Added diagnostics can affect timing.
Peak snapshots preserve the pending requests and last acknowledged sequences for both threads.
Sample index ranges use the complete sample array, including warmup observations.
These records do not measure debt continuously or establish exact debt age.

The archive includes `AnalyzeTiming.ps1`. Run it with `-SourceRoot` set to the measured checkout to regenerate `timing-analysis.json`.
The script checks 19 source/binary hashes and reconciles credits and waits across 12 phases.
It also checks peak values, request identities, timestamp ordering, sample ranges, and final ledger retirement.
All archive members match their original files by SHA-256.

Keep the earlier [capacity failures](sampled.md). A later passing case does not replace them.
See [the controller contract](../../../lode/profiling/sampled-signals.md) and [native signal limits](README.md).
