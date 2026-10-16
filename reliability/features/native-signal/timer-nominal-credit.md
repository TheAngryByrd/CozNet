# Nominal timer credit capture

Evidence: `artifacts/timer-nominal-credit/20261016T131125614-32b1c50d`.

The declared Linux x64 capture uses nominal-event credit, managed preparation, native preparation, and the 10 µs initial timer interval.
The runner retains both cases. It returns exit 1 because the treatment fails the existing debt gate.
All source, binary, and retained replay hashes remain unchanged.

| Case | Workload exit | Counter and credit replay | Selected peak debt (ns) | Peer peak debt (ns) | Native close |
| --- | ---: | --- | ---: | ---: | --- |
| 0% | 0 | Pass | 0 | 0 | Complete |
| 50% | 1 | Pass | 303913 | 2361731 | Complete |

Both preparations record an 80,000 ns synthetic period, 40,000 ns credit, 30,000 ns selected wait, and 70,000 ns peer wait.
Both live measurements start with zero credit and epoch 1. Every retained live sample has a 1,000,000 ns period.
The zero-speedup case has 1,913 qualified events and zero credit.
The treatment has 1,026 qualified events and 513,000,000 ns credit across 18 recorded epochs.
Neither case excludes first events because of an epoch boundary.

The treatment retains `The service lease debt exceeds its capacity bound.` during measurement.
Its 2,361,731 ns peer peak exceeds the unchanged 2,000,000 ns limit.
Counter and credit replay remain valid for the failed record. They do not convert the failed workload into a passing case.
No unchanged timing retry or matched prediction series follows this capture.

## Limits and next step

This capture verifies successful nominal preparation, live reset, policy binding, and retained accounting replay.
It does not establish stable capacity, exact source membership, general CLR safety, or physical prediction accuracy.
The receipt audit below identifies a delayed-credit burst. The scheduler diagnostic identifies controller preemption in a separate run.
All debt, lease, child, handler, and receipt bounds remain unchanged.

The Release workload build, portable timer checks, and strict documentation build pass.
Python checks pass: counter 31, membership 15, matched prediction 35, and bound selected payment 110.
Both adversarial reviews identified the mutable top-level period on failed preparation. The implementation now declares the fixed live period.
Injected preparation failure remains untested. Failed preparation can retain synthetic rows that live replay rejects while preserving raw failure details.

See [the policy reference](../../../docsSrc/Reference/nominal-event-credit.md), [capture procedure](../../../docsSrc/How_Tos/run-nominal-timer-credit.md), and [estimator explanation](../../../docsSrc/Explanations/nominal-event-credit.md).

## Receipt audit

Audit: `artifacts/timer-nominal-credit-audit/20261016-first/failure.json`.
The adjacent retained script checks the raw treatment record and records its input hash.
It reconciles credit, global delay, released waits, and the failure debt without changing them.

Five accepted events post 2,500,000 ns of credit after the last preceding boundary.
Their source timestamps span 3,999,426 ns. Their managed receipt timestamps span 10,344 ns.
The gap from the previous accepted receipt to the first of these receipts is 5,212,683 ns.
All six observations use epoch 18. A resumed first-event transition does not explain this burst.

Peer command 1191 completes 4,440,348 ns before the first late receipt.
No later journal starts. Both workers have no pending command at the frozen endpoint.
Released waits stay unchanged between the preceding boundary and the failure boundary.
The resulting peer debt is `513002225 - 510640494 = 2361731` ns.
This is not a pending native-payment stall at the failed reservation.
The record has no scheduler capture. It cannot identify the cause of its receipt gap.

## Scheduler diagnostic

Evidence: `artifacts/timer-nominal-scheduler/20261016T132029579-46ab069d`.
This declared diagnostic adds scheduler capture to nominal credit without forced GC.
The same timer interval, preparation, workload duration, and bounds remain in effect.
It runs each case once and retains both outcomes. Inputs remain unchanged.
Zero speedup passes. Treatment fails the service lease debt gate at 6,499,906 ns peer debt.
Both counter and credit replays pass. Both cases complete native close and scheduler shutdown.

The retained `receipt-audit/audit_scheduler.py` uses copied counter, membership, and scheduler parsers.
It verifies distinct thread identities in one process, sample identity, clock metadata, and assured scheduler collection boundaries.
Role binding also uses the manifest-matched producer sources retained under `receipt-audit/producer`.
`SampledTimerWorkload` registers index 0 on the controller and index `slot + 1` on each target.
`SchedulerCaptureGroup` preserves those indices. The Linux session captures the calling thread, and the factory supplies no target override.
Thus indices 0, 1, and 2 identify controller, selected, and peer. `producer-binding.json` records the four source hash matches.
The treatment has 244 controller records, 900 selected records, and 1,020 peer records.
All three streams have zero final loss, no reported failure, and no censored interval.

| Interval | Wall interval (ns) | Controller switch-out overlap (ns) | Selected switch-out overlap (ns) | Peer switch-out overlap (ns) |
| --- | ---: | ---: | ---: | ---: |
| Previous accepted receipt to first late receipt | 13959906 | 13800756 | 0 | 267128 |
| First through last late receipt | 56044 | 0 | 0 | 36000 |

The controller interval has the preemption flag.
Thirteen events in epoch 24 span 12,018,855 ns at source and post 6,500,000 ns of credit in 56,044 ns.
The preceding accepted event also uses epoch 24.
The previous global delay is 745,092,909 ns. The frozen global delay is 751,592,909 ns.
Final peer released wait is 745,093,003 ns, with no active wait.
Thus peer debt is `751592909 - 745093003 = 6499906` ns.

The evidence supports controller descheduling as the source of this diagnostic run's sample backlog.
It does not identify why the controller was preempted or explain the earlier run without scheduler data.
Clock comparisons assume no unobserved fast-timekeeper anomaly. The observed clock checks do not establish a general error bound.
The later interval from the final receipt to the failure boundary includes failure handling. It is not a normal-service timing measurement.
Scheduler capture changes observer cost. This run does not quantify that cost or establish stable capacity.

## Next architectural question

The current timer path requires the managed controller to consume samples before it publishes credit and reserves payment.
A delayed controller can therefore publish credit that already exceeds the finite debt limit.
Splitting that backlog into smaller updates would change execution. It would not prove that debt stayed bounded during the gap.
Priority or affinity changes would not guarantee that the controller always runs in time.

The next design step is to compare target-local sample processing and payment ownership with the current controller dependency.
The [upstream scheduling comparison](../../../docsSrc/Research/native-owner-scheduling.md) describes the relevant native Coz and JCoz paths.
Any prototype must preserve source admission, eligibility epochs, exact-once accounting, measured wall-time payment, and CLR lifetime checks.
The existing bounds remain unchanged. No matched series or claimed capacity fix follows these diagnostics.
