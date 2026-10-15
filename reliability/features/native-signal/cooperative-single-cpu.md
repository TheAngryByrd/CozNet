# Cooperative payment on one guest CPU

This diagnostic constrains the existing managed sampled workload to guest CPU 0.
It tests capacity under shared CPU execution. It does not test prediction accuracy.

## Method

Local capture: `artifacts/bound-child-cpu-single/20261015T204154225`.
Baseline: `artifacts/bound-child-cpu-50k/20261015T203140741-db867626`.
The diagnostic reuses the baseline profiler, native cooperative library, and managed workload binaries.
All retained source and binary hashes match before and after execution.
The image identity is `sha256:3db72bd242b5f26a45d74c2d49a7654740e75c732654658177e52aa498645c14`.

An unrestricted probe reports guest CPUs `0-3`.
The diagnostic adds Docker `--cpuset-cpus 0` to all three live cases.
A probe with the same CPU-set restriction reports `Cpus_allowed_list: 0`.
The recorded command arrays retain the restriction for each workload container.
The CPU-set restriction also changes the processors available to runtime policy.
This is not an isolated test of one scheduler setting.

The workload requests 50,000 batches with qualified-sample cadence, child CPU capture, and pause timing.
It keeps the 2 ms debt, 50 microsecond child-request, 5 ms handler, and 100 ms receipt limits.
Each process has a 30-second watchdog.
The three cases run serially. No unchanged failed case is retried.
The runner copies itself into the local capture and uses the retained baseline analyzer.
These ignored local artifacts are not distributed with this document.

## Outcomes

| Case | Runtime result | Replay result |
| --- | --- | --- |
| Zero speedup | Completed without payment | Evidence verified |
| 50 percent | Failed the peer debt limit after 8,538 selected batches | Evidence verified; failure preserved |
| Expected invalidation | Preserved the expected rejection | Evidence verified |

Treatment reports peer debt of 2,071,535 ns.
Global delay is 13,730,093 ns and accepted peer payment is 11,658,558 ns.
The difference equals the reported debt.
The failed run remains failed even though replay verifies its evidence.
Final verification retains `The sampled source did not complete.` after the failed body.
Cleanup records no error. The peer and controller threads join before close.

## Wait and pending-request evidence

Peer command/generation 3, child 9 requests 50,000 ns.
Its measured wait is 939,001 ns and its handler interval is 939,676 ns.
The enclosing CPU observations advance by 35,700 ns.
CPU observations include work outside the measured wait and do not establish calibrated clock accuracy.

Peer command/generation 27, child 10 has the longest retained handler interval: 1,037,892 ns.
Its measured wait is 37,445 ns and its CPU delta is 37,900 ns.
The extra wall duration therefore lies mainly outside the measured payment interval.
The record does not locate that interval within the remaining handler work.
No retained handler exceeds the 5 ms limit.

At failure capture, peer command/generation 34 has a 71,535 ns budget and no recorded child.
Its native capture timestamp is 11,365,728 ns after the arm timestamp.
The cleanup journal places the first child start 13,098,192 ns after arming.
These intervals use one native clock domain.
They show that no child was recorded by the failure snapshot, followed by later service.
They do not show whether the peer was runnable, blocked, executing application code, or delayed by the host.

Cleanup retains two children with 71,589 ns of measured wait.
That later wait does not repair the failed experiment or enter its accepted payment total.
The cleanup capture occurs 333,735,357 ns after arming and cannot establish an on-time first receipt.
All captured CPU readings are observed readings. Replay reports no clock-consistency rejection.

## Buffered credit at the debt boundary

The last selected and peer settlements finish before request 34 starts, using the same managed timestamp domain.
No later payment is accepted before the failure.
The final four credited samples, indices 120 through 123, each add 500,000 ns.
The pending budget plus those credits is exactly 2,071,535 ns.
The managed interval from request start to the debt observation is 1,660,828 ns.

Those four source timestamps precede native request arming by 3,976,999, 2,977,019, 1,976,661, and 976,495 ns.
`PerfAttribute.TaskClock` selects `CLOCK_MONOTONIC` through `use_clockid` and clock ID 1.
`CooperativeSpinTiming.now` also reads `CLOCK_MONOTONIC` on Linux.
This ordering uses that shared clock contract. It is not an independent calibration.

The credits describe buffered samples, rather than four new sample periods after native arming.
The capture does not record each ring-drain or managed publication timestamp.
It therefore cannot divide the backlog among collector scheduling, owner scheduling, and publication work.

## Interpretation and next question

The constrained run demonstrates a capacity failure with valid retained evidence.
It does not reproduce the earlier 5 ms selected-handler failure.
The wall/CPU divergence is consistent with time not charged to the owner CPU clock.
It does not distinguish OS descheduling, host scheduling, or accounting error.
The missing child interval needs target scheduling evidence before it can receive a specific cause.

The next experiment should test owner-driven sample draining before debt checks.
Native Coz processes the target sampling buffer before its delay check.
A controlled owner-driven mode could remove one collector scheduling dependency.
It must preserve sample ordering, loss detection, eligibility epochs, and payment exclusion.
It must also measure probe overhead and retain all timing failures.
This is a proposed comparison, not a demonstrated capacity fix.
Keep the current failure and bounds. Do not replace measured wall-time payment with CPU time.

See [CPU capture contracts](cooperative-child-cpu.md) and [upstream scheduling differences](../../../docsSrc/Research/native-owner-scheduling.md).
