# Target-local spin before a queue wait

## Finding

In transaction run `20261019T121118409-e589636a`, the median mean handoff is 205.60 microseconds in the plain original arm and 51.34 in the profile baseline. The median gaps in handoff and in wall latency are close.
Hypothesis, not shown: a consumer that blocks on an empty queue wakes later on an idle host than on a host that the profiler keeps busy.

## Decision

`TargetQueue(capacity, spin)` spins up to `spin`, from 0 to 1 ms, in a `Dequeue` wait before it blocks, as `TargetLock(spin)` does. `Enqueue` does not spin. `TargetQueue(capacity)` keeps a spin of 0, so its behavior does not change.
The spin runs inside the wait, between `WaitBegin` and `WaitEnd`, so a spinning consumer is blocked for the ledger. The wake rule and the bounds do not change.
A new `spintransaction` suite runs the transaction workload with a 1 ms spin in both arms. The `transaction` suite stays unchanged, with a spin of 0.
The sample modes are `spin-transaction-profile` and `plain-spin-transaction`. `VerifyTargetLocalProfile.ps1 -Workload spintransaction` and the build target `TargetLocalSpinTransactionVerify` run it.
If the hypothesis holds, the spin shortens the plain handoff more than the profiled handoff, and the gap in baseline latency falls.
A smaller gap alone does not show the cause. A spinning consumer also uses a full CPU, so it changes the load, the contention with the producer, and the power state of the 4-CPU host.
The profiler also samples a spinning consumer, so a spin can raise the handler cost of the profile arms. The suite records the handler time, so the runs show its size.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 92 and change none.

93. Unit tests show that a spinning `Dequeue` takes an item that arrives during the spin, counted by an internal count of the takes in a spin, that a spin outside 0 to 1 ms raises `ArgumentOutOfRangeException`, and that the spin shares the timeout budget.
94. Each spin transaction record follows criteria 83, 87, and 90, with mode `spin-transaction-profile` or `plain-spin-transaction`.
95. Each of the six spin transaction points has 5 valid blocks, a median absolute difference of at most 5 percentage points, and each block within 10. The positions are balanced as in criterion 57.
96. The status reports the median mean handoff and the median wall latency of the profile baseline and the plain original arm, for the spin suite and the `transaction` suite on the same tree. It states a cause only if the records show it.
97. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local transaction handoff](target-local-transaction-handoff.md), [target-local spin lock](target-local-spin-lock.md), and [target-local status](target-local-status.md).
