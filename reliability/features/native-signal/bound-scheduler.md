# Bound scheduler evidence

## Ordinary CPU set

Retained run: `artifacts/bound-scheduler/20261016T001158200-7740bbc1`.
The runner requests 5,000 batches with qualified-sample cadence, owner draining, direct readiness, peer progress, and scheduler capture.
Source and binary hashes remain unchanged throughout the run.
The 14 native checks and three managed check routes pass on Linux.

Zero speedup and treatment complete. Invalidation after selected payment passes its intended rejection check.
All three records pass selected-payment and scheduler replay with zero reported loss.
Zero speedup reports no peer query windows.
The treatment retains command 34, generation 34, ordinal 1, phase `wait_entered` at its latest submission query.
No failed debt gate occurs. This run does not explain the previous debt failure.

## One CPU

Retained run: `artifacts/bound-scheduler-single/20261016T001320738/01-direct`.
The retained runner verifies CPU 0 affinity and reuses the preceding source and binary manifest.
It requests 50,000 batches. CPU affinity also changes runtime CPU policy.
This is a diagnostic scenario, not an isolated overhead comparison.

Zero speedup passes. Invalidation after selected payment passes its intended rejection check.
Treatment fails at 2,000,132 ns of peer debt.
All three records pass evidence replay. The aggregate result remains failed.

The failed treatment binds peer command 167, generation 167, and a 132 ns request budget.
Its submission and failed-debt markers both report `pending`, ordinal zero.

| Observation | Managed timestamp at 1 GHz |
| --- | --- |
| Request start | 457486650116416 |
| Debt observation | 457486654181357 |
| Debt query start | 457486654358516 |
| Debt query finish | 457486654359084 |

The clock model bounds the scheduler offset to [-115, 708] ns under the equal-rate assumption.
The peer stream records a preempted switch-out interval [457486650018822, 457486659945950) in native nanoseconds.
This closed observed interval contains the entire mapped request-to-debt window and both query windows, including their uncertainty bounds.
The request-to-debt duration is 4,064,941 ns.
The conservative observed switch-out overlap range is [4,064,118, 4,064,941] ns.
The later debt query begins 177,159 ns after the debt observation.
Its marker does not independently establish the phase at the earlier debt timestamp.

The records support an off-CPU explanation for this pending request under the stated clock assumptions.
They do not establish general scheduling cause, stable capacity, or prediction accuracy.
They do not authorize debt forgiveness or replace wall-time payment.
The current treatment still fails the existing 2 ms limit.

## Scope

Windows managed capture checks pass. Linux live collection passes the evidence checks above.
Windows live scheduler capture has no backend in this change.
The earlier Windows timer-layout allocation failure remains unattributed and was not retried unchanged.
Claude and Codex cleared the change after review.
Codex reviewed supplied source packets and partial raw records because its filesystem tools could not start.
Neither reviewer executed the validation commands.

## Accepted diagnostic limits

Portable checks exercise the capture wrapper and shared retirement gate.
Workload registration order and the Linux backend have live coverage, without a portable workload-level fault injection check.
A future wiring error can therefore require live replay to expose it.
This coverage limit is accepted for the opt-in diagnostic. It has no accounting or payment authority.

A failed collector disposal retains target ownership and prevents worker and controller joins.
The process can retain threads until exit. The runner watchdog bounds the process lifetime.
The portable disposal check verifies the gate, but does not inject that failure into the complete sampled workload.
Retaining ownership is required because collector shutdown remains uncertain.

Failure before the initial clock anchor produces incomplete scheduler evidence.
Replay rejects that evidence even when it retains the original workload failure.
This behavior prevents incomplete diagnostics from supporting an overlap claim.

Clock rate equality remains an explicit assumption.
Neither clock brackets nor source review establish an independent physical clock-error bound.

Fifteen Python checks cover scheduler parsing, overlap, fractional conversion, and intersecting clock anchors.
The focused failed-debt window checks substitute the full accounting replay with an explicit result.
They exercise window construction without claiming that their reduced fixture passes full selected-payment replay.
Matching progress fields pass the real progress validator. Mismatched requests fail that validator before the defensive window check.
The retained live failure supplies the full accounting replay evidence for the matching path.

See [the reference](../../../docsSrc/Reference/bound-scheduler-capture.md)
and [the progress marker evidence](peer-progress-marker.md).
