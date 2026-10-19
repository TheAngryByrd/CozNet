# Target-local transaction delay

## Finding

Transaction run `20261019T110105861-414dba20` fails all six points of [the latency point plan](target-local-transactions.md). Its real gains follow the model, but the `peer` predictions stay near 5% at each speedup.
Hypothesis, not shown: the `E / T` rule assumes that the global delay stretches each transaction as it stretches the whole run.
In the pipeline, a `peer` sample adds global delay while the consumer runs, and the producer pays it in its next transaction. So the delay does not fall evenly across transactions.

## Decision

The virtual latency of one transaction is its wall latency minus the global delay that was added between its begin and its end. This is its length in the virtual time of Coz, which is wall time minus global delay.
Hypothesis, not shown: this is the delay that a real optimization would remove from that interval. Global delay from work off the dependency path of a transaction can overlap it without slowing it.
- `TargetProgressCounter` keeps the begin and end time of each transaction that ends.
- After the experiment, the profile rebuilds the global delay from the journal. The ledger records a publish only when it raises the global delay, with its time and the new value. So the global delay at time t is the largest value of the publishes at or before t, or 0. Publishes in time order can carry a lower value after a higher one, because the ledger stamps each publish after its swap.
- `TargetCausalExperiment` gains `VirtualLatencyNs`, the sum of the virtual latency of the completed transactions.
- The prediction for a `Latency` point becomes `1 - (VL / V) / (VL0 / V0)`, where VL is `VirtualLatencyNs` and V the departures. The real gain stays `1 - W / W0`.
The perf samples and the journal use `CLOCK_MONOTONIC` in nanoseconds. On Linux, `Stopwatch` reads the same clock. So the transaction times and the journal times share one clock.
A transaction whose virtual latency would be negative counts as 0, and the record counts such transactions in `NegativeTransactions`.
Two timing limits apply. A thread pays its debt at its later settlements. The 2 ms bound limits the size of the debt, not the time until it is paid, so delay can move between neighboring transactions.
The ledger stamps a publish after it raises the global delay, so a preemption between the two moves the delay to a later time.
The transaction list is presized to 4096 entries. A longer or faster experiment grows the list inside the measured work.
`LatencyNs`, `Arrivals`, and the `E / T` value stay in the record, so the two rules can be compared.

The workload, the arms, and the model of the latency point plan do not change. The rule and the bounds do not change.

## Acceptance criteria

These criteria are fixed before the first run. For runs on this tree, criterion 86 replaces the prediction formula of criterion 82, and criterion 88 replaces criterion 84. Criteria 82 to 85 stay as the record of the first transaction run. The other criteria do not change.

86. Unit tests show that the global delay is the largest value of the publishes at or before a time, that each virtual latency subtracts the global delay added during its transaction, that a negative value counts as 0 and is counted, and that the prediction is `1 - (VL / V) / (VL0 / V0)`.
87. Each transaction profile record holds `VirtualLatencyNs` and `NegativeTransactions` for each experiment, with the other fields of criterion 83.
88. Each of the six transaction points has 5 valid blocks, a median absolute difference of at most 5 percentage points, and each block within 10. The positions are balanced as in criterion 57.
89. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local latency progress points](target-local-transactions.md) and [target-local status](target-local-status.md).
