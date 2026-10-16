# Prediction gap diagnostics

These diagnostics retain the existing estimator and payment bounds.
They distinguish arithmetic sensitivity, an incomplete component capture, and a separate instrumentation candidate.

## Fixed-record CPU sensitivity

Source: `artifacts/probe-closure-comparison/20261016T104424164-d8d7dd522fd848ccab14dae53bf00543`.
Calculation: `artifacts/prediction-gap-sensitivity/20261016T105935720`.
The retained script replays the four-arm capture before calculation and records input hashes.
It uses exact rational arithmetic and rounds interval endpoints outward for JSON output.

Treatment consumes 2,396.9494 ms of selected-thread CPU.
Sampled zero consumes 2,255.4106 ms. The difference is 141.5388 ms.
Recorded sample credit is 1,026.5000 ms. Frozen global delay is 1,027.423641 ms.
With recorded durations and progress fixed, matching the physical reduction requires another 308.6493–308.9964 ms of global delay.

A separate substitution replaces sample credit with half of the treatment's measured selected CPU.
It retains the recorded global-delay contribution above sample credit.
The substituted global delay is 1,199.398341 ms.
The resulting descriptive reduction is 46.8592–46.8736%.
The physical result still exceeds it by 6.0547–6.0703 percentage points.

This substitution includes profiler and selected-payment CPU.
The CPU readings cover a narrower interval than the measurement boundaries.
No sampling, scheduling, payment, or program response is simulated.
The result is neither an accuracy bound nor a feasible intervention.
It does not justify using thread CPU as selected-method credit.
It leaves treatment execution cost and instrumentation cost as separate investigation targets.

## Long component capture

Capture: `artifacts/component-cpu-long/20261016T105209001-d514d489`.
The command requests 500,000 selected batches with interval 8192, nominal-event credit, prepared receipts, and owner/controller execution.
It enables finite measurement, owner draining, direct receipt readiness, and component CPU capture.

Zero speedup completes. The treatment fails after 192,529 batches at 2,499,837 ns of peer debt.
The limit remains 2,000,000 ns.
Expected invalidation passes. All four Linux preflight groups pass, and input hashes remain unchanged.
Replay verifies all three evidence records and preserves the treatment failure.
The runner and replay exit 1. No timing retry replaces the failed capture.

The incomplete treatment retains these measurement-subset observations:

| Activity | Calls | CPU bracket total (ms) | Largest CPU bracket (ms) |
| --- | ---: | ---: | ---: |
| Drain | 385,059 | 135.3982 | 6.6707 |
| Skipped service | 253,225 | 47.2594 | 0.2457 |
| Idle service | 309 | 1.7977 | 0.8600 |
| Pending service | 130,588 | 53.1630 | 1.3878 |
| Receipt | 991 | 28.8356 | 8.6775 |
| Submit | 992 | 11.7286 | 2.7973 |
| Receipt and submit | 1 | 0.0205 | 0.0205 |

Each observation includes diagnostic work and clock cost.
The maxima have no retained call timestamp, so they cannot locate the failing call.
The incomplete treatment and completed zero arm have different work counts.
Their totals do not establish a paired component-cost difference or the cause of the debt failure.

## Probe placement component comparison

Capture: `artifacts/entry-probe-cost/20261016T105853147`.
The scratch native candidate emits both bound probes before the controlled method body.
It omits the repeated loop countdown and preserves the full binding guard.
It accepts only bound Linux-perf target selection, registered mapping, interval 8192, and the ordinary placement input.
Its placement export returns diagnostic value 2. The normal profiler remains unchanged.

Eight shuffled pairs each execute 50,000 checksum batches after 2,000 warmup batches.
Both candidates execute 100,000 bound probes per case.
All 16 cases pass checksum, identity-count, cleanup, and duplicate-module-invalidation checks.
Input hashes remain unchanged. Calling-thread allocation is zero in every measured case.
The median paired elapsed reduction is 20.189512 ms, or 11.2602%.
All pairs favor the entry candidate, with reductions from 0.1441% to 18.2002%.
Six pairs execute the countdown case first. Order and scheduling can affect the difference.

This comparison changes probe placement as well as loop instructions.
It excludes sampling, active payment, and debt accounting.
It does not isolate one instruction's cost or establish prediction accuracy.
Entry probes cannot service a request that arrives during a long-running invocation.
The controlled short method therefore does not establish a general entry-only instrumentation policy.
The retained native sources and harness identify this candidate separately from the normal profiler.

## Entry-probe physical comparison

Capture: `artifacts/entry-probe-comparison/20261016T110040157-d372b64795214e6692097e341b0aed57`.
The scratch runner records `probePlacementDiagnostic=two-entry-probes-no-loop-countdown` in its plan.
It retains the actual candidate profiler source, binary, and runner in its hashed snapshots.
The normal comparison analyzer checks accounting but does not validate this candidate's probe-coverage policy.

All four arms complete 500,000 selected batches with no invalid arms, integrity errors, or runner errors.
The sampled effective elapsed reduction is 30.9794–30.9953%.
The physical reduction is 49.9535–49.9550%.
The descriptive shortfall is 18.9582–18.9757 percentage points.
Selected CPU is 2,029.9794 ms at zero speedup and 2,355.7277 ms in treatment.
Treatment records 950.5 ms of sample credit, 338 selected pauses, and 2,232 peer payments.
The runner exits 1 because prediction remains unqualified.

This is a separate execution from the countdown comparison.
It cannot isolate a regression or improvement caused by placement.
It does establish that the candidate has not resolved the prediction gap in this block.
The component speedup alone does not justify removing loop coverage.
The normal countdown profiler remains in use.

See [the CPU explanation](../../../docsSrc/Explanations/selected-cpu.md).
