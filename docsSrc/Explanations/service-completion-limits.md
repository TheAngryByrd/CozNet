# Limits on valid service completion

A finite service lease reserves a fixed delay budget.
New global credit does not increase that budget.
Each accepted child contributes its measured wait.
The journal stops when its total reaches the budget, or when a child reports deferral or interruption.

Let `B` be the budget and `H` the maximum accepted handler duration.
The accounting contract uses integer nanoseconds and `H = 5,000,000 ns`.
Before the last child, a continuing journal has measured at most `B - 1 ns`.
The last child can contribute at most `H`.
Thus, a valid journal totals at most `B - 1 ns + H`.
The requested 50,000 ns child wait is not the overshoot limit.

For example, a 50,001 ns budget can accept 50,000 ns before its last child.
That last child requests 1 ns but can measure 5,000,000 ns under the existing handler gate.
The resulting total is 5,050,000 ns.
This is a contract example, not a measured run or a recommendation for acceptable overhead.

If a journal has already stopped, its accepted sum is exact for valid completion.
A stop below the budget does not permit more accepted payment.
Deferral, interruption, and cancellation do not forgive unpaid debt.

## Conditional local-credit range

Let `L` be settled local credit and `A` the accepted unsettled wait.
For an active service, valid completion places local credit in `[L + A, L + B - 1 ns + H]`.
For a stopped service, both endpoints equal `L + A`.
An idle runnable thread retains exactly `L` if no new work enters accounting.

These ranges assume no new samples, reservations, or other ledger transitions except completion of the existing service.
They describe successful completion states only.
An unfinished native handler can exceed the gate and invalidate its journal.
The range does not establish that it will finish successfully or bound its actual unobserved execution.
Native receipt validity, complete journal inventory, and workload success remain separate requirements.
The [retained ledger capture](../../reliability/features/native-signal/ledger-boundaries.md) demonstrates this distinction.
Its treatment records a 9.684 ms wait, rejects the journal, and leaves native ownership unresolved.
The conditional valid-completion limit cannot qualify that failed run.

The range also does not describe global delay.
Settlement adds wait locally, and a later catch-up can publish a local lead.
Concurrent local amounts cannot be summed into global delay.
Further reservations and overshoot can create additional effects outside this fixed-service range.

This conditional result is not a prediction-error bound.
The measurement estimator and accuracy gates remain unchanged.
See [the range reference](../Reference/service-completion-range.md) and [experiment boundaries](experiment-lifecycle.md).
