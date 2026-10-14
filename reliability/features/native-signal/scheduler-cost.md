# Scheduler capture cost

A fixed-work comparison does not detect a clear elapsed-time or process-CPU difference from scheduler capture in this busy loop.
This result does not establish zero overhead or bound overhead in the sampled signal workload.

## Method

Three managed threads each execute 300,000 calls to the same synchronous arithmetic method.
Every thread must produce checksum `14998500000000`.
Disabled cases create no scheduler events or collectors.
Enabled cases open one event and start one collector per workload thread before measurement.
The targets remain alive until capture stops.

Eight blocks contain one disabled and one enabled process each.
Seed `20261021` fixes the order within blocks. Each process has a 30-second watchdog.
The processes run in the same Linux SDK image with tiering, ReadyToRun, and startup profiling disabled.
No case sends native signals or forces GC.

The measured interval starts before the controller releases the two workers.
It ends after all three threads complete their fixed work.
Process CPU covers the process, including collector threads.
Capture setup precedes measurement. Capture cleanup follows measurement and has a separate elapsed value.
This measures collection cost during fixed work. It excludes capture startup and shutdown cost.

```powershell
dotnet run --project build/build.fsproj -- -t LinuxSchedulerCost
```

The retained run invokes `native/MeasureLinuxSchedulerCost.ps1` directly after building the workload.
The FAKE build project compiles with this target.

## Results

All 16 processes complete with matching checksums and successful cleanup.
All 24 enabled thread streams pass the analyzer with zero final loss.
Enabled processes produce 210 to 598 records each.

| Paired increase | Mean | 95% interval |
| --- | ---: | ---: |
| Elapsed time | −0.56% | −2.23% to +1.10% |
| Process CPU | +0.07% | −1.30% to +1.45% |

Each pair uses `(enabled / disabled - 1) * 100`.
The intervals use the sample standard deviation of eight paired percentages and Student's t with seven degrees of freedom.
Both intervals include zero. Negative estimates do not establish that capture improves performance.

Disabled elapsed times range from 930.14 to 988.60 ms.
Enabled elapsed times range from 932.35 to 943.58 ms.
Enabled capture cleanup takes 4.86 to 6.18 ms and is excluded from those intervals.

The [archive](data/scheduler-cost/run-20261014T050513649.zip) preserves the source snapshot, executable, order, outcomes, records, and analysis.
All 65 archived files match their retained originals.
The archive SHA256 is `7b74e7dbfaec81b3bf55e3c67101a6c1ee78d50e3bc7f2ba6686e659ef9cc8ad`.
The kernel is `5.15.153.1-microsoft-standard-WSL2`.
The runner snapshots the workload before execution and retains failed outcomes without retry.
The current runner verifies snapshot hashes and runs the snapshot's analyzer after all cases finish.
Any failed child, missing record, changed snapshot, or analyzer failure fails the target.
The retained run predates this runner gate. Its 16 child exits and separately executed analysis all pass.

## Startup and evidence checks

The current workload counts successful worker starts and joins only those threads.
It completes the countdown slots for workers that could not start.
It disposes worker synchronization objects only after every started worker joins.
The timeout branch has source review coverage. The fixtures below exercise failed starts, not a delayed worker after a timeout.
`SchedulerCostWorkloadChecks.FailedStartsRetainEvidence` injects failure at the first and second worker start.
Both cases retain failed JSON output, stop cleanly, and pass on Windows and Linux.
These checks do not repeat the timing comparison.

Six runner checks cover successful evidence, failed children, missing records, changed or added snapshot files, and analyzer failure.
Replay with the retained capture returns failure because its GC treatment failed.
Replay with the retained cost comparison returns success.
See the [final check archive](data/scheduler-capture/final-checks-20261014T051450099.zip).

## Limits

The comparison covers one busy-loop workload and one Linux environment.
It does not estimate costs during forced GC, signal handling, high switch rates, or collector stalls.
The earlier sampled treatment produces more records and still fails its debt bound.
No causal-prediction or CLR-safety claim follows from this result.
Keep the timing limits, retained failures, and two-percentage-point prediction gate unchanged.

See [scheduler capture](scheduler-capture.md) and [scheduler contracts](../../../lode/profiling/scheduler-records.md).
