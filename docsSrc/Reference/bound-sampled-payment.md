# Sample-driven bound peer payment

`BoundSampledPaymentWorkload` connects live CLR-bound CPU samples to the existing ledger and cooperative receipt adapter.
It is an internal capacity diagnostic. The public serial API does not use this path.

## Owners and credit

The selected owner executes `CpuWorkload.Run`, collects its task-clock samples, and runs the controller.
A separate peer owner executes the same method and services native requests through bound loop probe 2.
The selected owner does not pay. Both owners use the same admitted CLR method binding.
The source factory and owner reader are portable hooks. The current command supplies the Linux x64 implementations.

The controller reads provisional sample suffixes without consuming the retained source records.
It converts selected nominal periods into credit at zero or 50 percent.
The first observation establishes eligibility epoch 1 and earns no credit.
Unmapped samples do not earn credit. Cumulative source counters remain separate from nominal credited periods.
Completion checks credit against the selected-period sum after the baseline.

Epoch 1 declares that this selected workload has no explicit payment or dependency wait.
It does not establish general thread eligibility, absence of runtime suspension, or original-application CPU attribution.
The selected method includes countdown instructions. Controller work also consumes the selected owner's CPU.

## Peer requests and receipts

The controller publishes selected credit through the existing ledger catch-up operation when the selected owner owes no debt.
It reserves peer service from the resulting ledger debt.
Every request retains its command, native generation, ledger budget, receipt observation, and final native copy.
The peer services at most one bounded child per loop probe.

This diagnostic accepts a receipt only after native visibility reaches Retired.
It does not subtract unconfirmed partial waits from observed debt.
Receipt acceptance preserves the existing site, lease, child, aggregate, and timing checks.
The controller releases native ownership only after accepted settlement and a retained final copy.

`BoundCooperativeClient.LastAccountingObservation` retains the latest observation returned by the native receipt parser.
The client clears this property before each active accounting attempt.
The property is diagnostic evidence. Its presence does not prove that ledger acceptance or the final CLR check succeeded.

## Gates and completion

`CooperativeDebtSnapshot.Capture` reads both thread observations from the same immutable accounting state.
Observed debt is `max(0, Global - Local)` for each thread.
The local ledger value includes settled waits as well as sample credit.
Retained credit and wait totals remain diagnostic fields; they do not determine the runtime debt gate.

An accepted child journal does not reduce ledger debt until its service settles.
Settlement adds measured wait to local delay and returns the thread to Runnable.
The next catch-up operation publishes any local excess to global delay.
An earlier snapshot retains its original values and operation state after these transitions.

The snapshot rejects duplicate thread handles and handles from another ledger.
It creates two immutable thread observations per call. Their cost in the sampled controller remains unmeasured.
The legacy command pays only the peer. The opt-in selected-payment command also settles selected leases during excluded callbacks.

| Gate | Bound |
| --- | --- |
| Requested selected work interval | Two seconds |
| Observed debt on either owner | At most 2 ms |
| Requested child wait | At most 50 microseconds |
| Child handler interval | At most 5 ms |
| Request and receipt interval | At most 100 ms |
| Final peer debt drain | Less than 100 ms |
| Process watchdog | 30 seconds |

Peer overshoot can advance global delay above selected credit.
The diagnostic retains this selected debt and applies the same 2 ms gate.
It does not forgive the debt or silently assign payment to the selected owner.
The final record can contain bounded selected residual debt. This is not complete Coz execution.

Normal completion requires accepted source closure, no ledger reservation, valid work and probe counts, joined peer ownership, and a final CLR check.
Zero percent must produce no credit or payment.
Fifty percent must produce positive credit and accepted peer payments that cover that credit.
The source's completed prefix must equal the provisionally consumed records.

## Invalidation and failure evidence

The invalidation case loads a second matching module after accepted real payment.
Both source completion and cooperative completion must reject that binding.
`rejectionVerified` means the expected rejection check passed. It does not mean that the experiment completed.
The record retains the accepted earlier payment, provisional samples, cleanup results, and any peer lifetime failure.
Native headers and children include their public fields in JSON.
`AnalyzeBoundSampledPayment.py` independently replays sample credit and sums retained child waits.
It checks source-prefix equality, native identities, receipt bounds, final-copy equality, and recorded debt arithmetic.
Each child request must match the remaining budget, capped at 50 microseconds.
Each first receipt capture must precede or equal its enclosing snapshot capture.
Child arm timestamps must be nonzero. Zero indicates a failed native clock read.
Final native cleanup must match the pending request or the last released payment.
It rejects missing native fields and cannot promote a failed runtime case to a passing case.
It binds each record to its required case and rejects accepted results with missing debt evidence.
It does not reconstruct unrecorded scheduling or prove that debt stayed bounded between observations.

Cleanup stops normal peer work before it requests cancellation.
Failures detected before cleanup produce `failureBoundary`.
Failures detected during cleanup or final verification keep their separate error fields.
`LastObservedDebt` is the latest ledger observation, not a new debt calculation at capture time.
The boundary retains the last command, generation, budget, and request timestamp.
`Capture.Started` and `Capture.Finished` bracket the diagnostic call with managed timestamps.
`Capture.TimestampFrequency` gives managed ticks per second. Native timestamps use nanoseconds in a separate clock domain.
Do not compare absolute managed and native timestamps without a clock mapping.
Exception formatting follows capture and does not delay the native snapshot.
`Capture.State` is 0 for no request, 1 for captured raw evidence, or 2 for a capture exception.
`Capture.Failure` preserves the original workload exception. `CaptureFailure` records a separate diagnostic exception.
`Capture.Raw` contains the native header, copied child prefix, native clock, and status without receipt acceptance.
Captured raw evidence can contain a failed native status. State 1 does not mean valid payment evidence.
An active snapshot excludes an executing child's uncommitted wait. The peer can advance during capture.
This diagnostic does not reduce debt, release ownership, or replace the original failure.

`CooperativeFailureCapture.CaptureAll` accepts an ordered set of diagnostic readers.
It reads every slot before it formats the workload exception or any capture exception.
A failed read does not prevent later reads.
Each result retains its own timestamp bracket, raw evidence, and capture error.
Result order matches reader order. An empty reader set returns no observations.
The results are separate observations. They do not establish an atomic snapshot or a managed/native clock mapping.
The legacy command supplies one reader to `CaptureAll`.
The opt-in selected-payment draft uses the multi-reader helper for slots 0 and 1.

Cleanup polls bypass measurement acceptance and preserve native ownership until final copy and release.
Cleanup attempts cancellation for every native slot, even when an earlier slot fails.
An aggregate error retains the original failures in slot order.
Cleanup polling requires a cancellation call that returned successfully for that slot.
A later failure does not erase an earlier successful cancellation.

## Selected-owner payment draft

`--sampled-bound-selected-cooperative <path> <percent> <library>` selects the experimental two-slot path.
The peer owns native slot 0. The selected owner owns native slot 1.
The native controller uses a separate thread. It cannot register as a payment target.
Controller operations run on that thread. Source control and selected polling remain on the selected owner.
Dispatch timeouts do not cancel queued controller work. Failed cleanup can require process termination by the external watchdog.
The source factory must return `IOwnerStoppedCpuEventSession` for this path.
The existing command retains its one-shot source and peer-only payment.

An admitted selected loop probe can enter an excluded callback when the ledger reports selected debt.
The callback processes the acknowledged final drain before it reserves at most one selected service lease.
It polls that lease on its owner until retirement, subject to the 100 ms receipt bound.
Receipt acceptance retains the existing 50 µs child and 5 ms handler bounds.
Settlement updates selected local delay. A subsequent catch-up publishes overshoot without reserving a second lease.
Sampling resumes after the callback returns and the CLR lifetime check succeeds.

Each sample retains its eligibility epoch. The first observation in each epoch earns no credit.
The credit stream retains its fractional remainder across these baselines.
Final peer settlement can leave bounded selected debt. It does not start an unbounded cycle of overshoot payments.

The draft writes `schemaVersion: 3`, selected receipts and waits, local ledger values, pause boundaries, and both slots' failure evidence.
`timestampFrequency` gives the managed clock units for payment and pause timestamps.
Each payment retains a parsed `ReceiptInterval` with its start, receipt observation, and clock frequency.
The interval cannot exceed 100 ms. The workload checks it before it applies the returned ledger settlement.
`Payment.Finished` follows native release and can include later cleanup work. It is not the receipt deadline endpoint.
A pause without `Resumed` does not prove successful source restart.
`AnalyzeBoundSelectedPayment.py` validates versions 3 and 4. The legacy analyzer accepts only version 1.
Version 4 describes the separate [fixed-work cadence comparison](bound-selected-cadence.md).
Retained version 2 runs use their saved analyzer because they lack explicit managed receipt intervals and clock frequency.
Version 3 checks sample epochs, retained rounding remainder, both payment journals, local ledger arithmetic, and pause ordering.
It binds selected payment to a completed producer-stop transition and source restart to callback return.
Counter transitions and pause boundaries use managed timestamps. Native receipt checks use native timestamps separately.
The analyzer requires matching frequencies before it compares counter and pause timestamps.
The analyzer does not infer a managed/native clock mapping.
Raw `TransportError` values remain signed. Invalid numeric fields do not become valid because JSON encodes them as booleans.
It validates retained observations and totals, not a complete journal of every ledger transition.
Earlier version 2 runs passed zero speedup, 50% speedup, and invalidation after selected payment.
One treatment run earned credit from only one selected interval. A run with no eligible selected interval fails the positive-credit requirement.
Frequent pauses can make this reach condition unstable. They do not establish acceptable overhead or prediction accuracy.
Portable checks cover cancellation failure isolation and polling permission.
Live native cancellation and release failures remain unverified in the selected path.
See [the retained evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/bound-selected-payment.md).
Failed evidence cannot undo earlier native waits. Any source, lifetime, receipt, work, or capacity failure rejects the experiment.

The diagnostic does not establish general CLR safety, acceptable profiler overhead, stable capacity, or prediction accuracy.
The [retained runs](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/bound-sampled-payment.md) include a capacity failure and a later complete-evidence pass.
See [the procedure](../How_Tos/run-bound-sampled-payment.md) and [the eligibility comparison](../Research/cooperative-sample-eligibility.md).
