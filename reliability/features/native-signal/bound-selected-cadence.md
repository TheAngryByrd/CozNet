# Bound selected cadence evidence

This comparison tests sample-qualified selected checks against continuous checks with the same selected work count.
It does not establish prediction accuracy or isolate each source of observer cost.
Both modes publish available samples and service the peer before deciding whether selected debt permits a pause.

## Contract and execution

Command:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxBoundSelectedCadence
```

The run uses the repository .NET 10 workload and Linux x64 native profiler and cooperative backend.
The order is continuous, qualified, qualified, continuous.
Each child runs zero speedup, 50 percent speedup, and invalidation after selected payment.
Completed cases execute 5,000 selected batches with the required checksum.
The peer continues independently, so its work count differs between cases.
The loop uses the existing probe interval of 1,024 iterations.

Bounds remain 2 ms observed debt, 50 microseconds requested child wait, 5 ms handler duration, and 100 ms receipt interval.
The process watchdog remains 30 seconds.
The asynchronous peer loop has separate five-second shutdown and join bounds.

## Initial comparison

Retained run: `artifacts/bound-selected-cadence/20261015T170543323-4b616aca`.

Both qualified treatments completed.
The continuous treatments failed because synchronous `Worker.Run` applied a five-second timeout to the whole peer loop.
This harness limit prevented a completed fixed-work comparison.
The failure remains retained. It is not evidence of a causal prediction failure.

The revised comparison queues the long-lived peer operation with `Worker.Start`.
Controller commands still use bounded synchronous dispatch.
The runtime retains the peer task and observes its completion and fault during work and shutdown.

## Revised comparison

Retained run: `artifacts/bound-selected-cadence/20261015T170928801-14ea2b74`.

| Order and mode | Treatment result | Selected batches | Selected work duration | Pauses | Mapped complete intervals |
| --- | --- | ---: | ---: | ---: | ---: |
| 0: continuous | Pass | 5,000 | 23,510.721 ms | 10,167 | 11 |
| 1: qualified | Pass | 5,000 | 185.161 ms | 27 | 9 |
| 2: qualified | Pass | 5,000 | 315.004 ms | 46 | 16 |
| 3: continuous | Fail | 1,170 | No completed endpoint | 1,944 | 5 |

Durations use `selectedWorkFinished - selectedWorkStarted` and the retained managed frequency.
They exclude source completion and the final peer drain.
Mapped complete intervals exclude the first sample in each observed eligibility epoch.
All four zero-speedup cases and all four invalidation cases passed.
Every child retained unchanged source and binary hashes, passing managed groups, and passing Python checks.
Replay verified all twelve evidence files and preserved the failed runtime treatment.
The overall comparison correctly reports failure.

The later runner review found a setting-mismatch gate that could retain an earlier success flag.
`Read-BoundSampledOutcome` now grants success only after all requested settings match and the process exits successfully.
Portable fixtures reject mismatched schema, mode, and work count, malformed or missing records, and failed processes.
The revised outcome helper and analyzer recheck all twelve retained records in `rechecked-round2/`.
Its copies, hashes, logs, and `results.json` preserve the continuous failure.
The recheck runs 18 legacy and 33 selected Python checks without another timing run.
The host managed group also passes after removing a redundant post-loop assertion and adding the maximum-count parser check.

The failed treatment retained peer command 1,947, child 12.
Its requested wait was 50,000 ns.
Its handler bracket was 8,910,149 ns, exceeding the unchanged 5 ms bound.
The native child has `Valid=0`; the journal has `Stop=5` and `Error=4`.
The controller rejected receipt acceptance. Both owners and the controller joined, and native ownership closed.
The peer probe failure remains in `cleanupErrors`. Cleanup did not convert the failed experiment into a pass.

## What this establishes

The qualified policy reaches selected and peer payments with substantially fewer pauses in these two treatments.
The original sample epochs, baseline exclusion, fractional credit, receipt bounds, and invalidation checks remain enforced.
The continuous pass proves that the asynchronous peer loop can remain active beyond the former five-second harness limit.

The comparison has two observations per policy, not a stable performance estimate.
Portable cadence checks cover permission consumption and epoch reset, but do not execute the full injected callback's operation order.
The retained live cases provide that integration coverage.
The failed continuous case has no comparable completed duration.
Peer progress differs, and sample counts do not measure exact profiler CPU exclusion.
Neither completed treatment predicts a physical optimization.
The next measurement must separate observer costs and then compare predictions with a matched physical work reduction.

See [the procedure](../../../docsSrc/How_Tos/run-bound-selected-cadence.md),
[the reference](../../../docsSrc/Reference/bound-selected-cadence.md), and
[the upstream cadence comparison](../../../docsSrc/Research/signal-sample-cadence.md).
