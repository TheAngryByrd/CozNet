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
Cleanup polls bypass measurement acceptance and preserve native ownership until final copy and release.
Failed evidence cannot undo earlier native waits. Any source, lifetime, receipt, work, or capacity failure rejects the experiment.

The diagnostic does not establish selected-owner payment, general CLR safety, profiler overhead acceptability, or prediction accuracy.
The [retained runs](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/bound-sampled-payment.md) include a capacity failure and a later complete-evidence pass.
See [the procedure](../How_Tos/run-bound-sampled-payment.md) and [the eligibility comparison](../Research/cooperative-sample-eligibility.md).
