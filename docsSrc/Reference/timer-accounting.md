# Timer journal accounting

This page describes the experimental adapter from target-local timer journals to the delay ledger.
The adapter contains no platform calls. The live timer backend requires Linux x64.

## Binding

`TimerAccountingBinding.Parse` binds one `SignalServiceLease` to a native generation, command, and instruction range.
The generation must be from 1 through `uint.MaxValue`. The command must be nonzero.
The instruction range must be nonempty, start above zero, and not wrap.

Each child uses this accounting sequence:

```csharp
(generation << 32) | ordinal
```

The ordinal must be from 1 through 100,000.
The sequence increases across generations for one target registration.
It is not a timestamp or CPU sample identity.
Each registered target requires its own ledger thread.
A new registration requires a new ledger thread.

## Accepted prefixes

`TimerAccountingAdapter.Accept` returns a new adapter and accounting state.
The caller must retain both values together.
The previous state remains unchanged if any record fails validation.

The adapter requires these conditions:

- Generation, command, and budget match the binding.
- Native error fields are zero.
- Raw children match the received children.
- Previously accepted children and first receipt bounds remain unchanged.
- Continuation arming occurs during the preceding handler interval.
- Handler starts follow their arm timestamp and the preceding handler finish.
- Requested wait, measured wait, handler duration, and first receipt satisfy the existing bounds.
- Wait outcome agrees with instruction eligibility.
- The native aggregate equals the accepted wait sum.

Repeated snapshots do not apply the same child twice.
A zero-wait child still advances the journal cursor.
A failed clock or late first receipt cannot settle debt.

## Settlement

Terminal visibility alone retains the ledger reservation.
Quiesced or retired visibility must report no possible delivery before settlement.
Completion requires the original budget to be serviced.
Deferral preserves unpaid debt. Interruption preserves measured partial wait.
Cancellation requires managed intent and a matching stopped journal.

Settlement includes accepted overshoot exactly once.
Selected credit published during service remains outside the original budget.
A settled adapter rejects further observations.
Native cleanup can release memory after rejected evidence, but it cannot turn that evidence into accepted ledger settlement.

## Verification scope

Portable checks use constructed journals and the real `SignalAccounting` implementation.
The live receipt fixture uses synthetic selected credit to reserve 50,000 ns of peer debt.
Its actual native journal contains one deferred child with zero wait.
Successful accounting releases the reservation and preserves all 50,000 ns of unpaid debt.
Late receipts and failed clocks retain the original reservation with zero confirmed wait.

The separate [managed payment workload](../How_Tos/run-managed-timer-payment.md) consumes paid journals during controlled arithmetic.
It checks repeated generations, exact accounting, stable instruction maps, and arithmetic progress.
Its optional GC mode requires a complete forced collection inside a recorded command interval and bounds each collection call to 250 ms.
That interval does not isolate a native wait or a runtime suspension.
These checks do not establish general CLR safety, sampled capacity, or prediction accuracy.
See [timer interop](timer-interop.md) for the ABI and [the verification procedure](../How_Tos/verify-timer-interop.md) for commands.
