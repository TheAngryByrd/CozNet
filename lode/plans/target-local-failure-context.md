# Target-local failure context

## Problem

[Failure records](target-local-failure-records.md) name the worker, the debt, and the time of each ledger failure. They do not show how the debt arose.
In latency run `20261019T090901082-03de2f25`, the selected thread fails with 2.58 ms of debt, with no collection and no recorded run-queue delay.
The selected thread earns credit for its own samples, so its debt rises only when the global delay rises above its local delay. That happens only when the other worker publishes a larger local delay.
Open question, not shown: a measured child payment of the other worker that is longer than its request raises its local delay, and its publish then raises the global delay.

## Decision

The window of a failure holds the timed journal events of all workers that come before it in sequence and no more than 10 ms before it in time.
Each `TargetCausalFailure` gains `ContextEvents`, the size of the window, and `Context`, its 64 latest events. One payment can append 40 children, so the 64 events can cover only about the last 2 ms.
Each context event keeps the worker, the kind, the observed global delay, the local delay before the event, the amount, the request, the detail, and the time from the failure.
Each failure also gains `Workers`, one summary for each worker over the whole window: its events, its children with their measured and requested sums, the child with the largest overshoot, and its last publish.
The journal already records each child payment with its request and its measured time, and each publish with the global delay that it observed. So the native code does not change, and no rule, bound, or criterion changes.
The analyzer copies the context of each failure into the summary. The evidence page reports the worker summaries of each failure.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 78 and change none.

79. Each failure of each profile record holds `ContextEvents`, `Context`, and `Workers`. Unit tests show that the context keeps the 64 latest events of the window in sequence order, with every field and the time from the failure, and that the count and the worker summaries cover the whole window.
80. For each debt failure in a run on the reviewed tree, the status names, for each worker in the window, the measured and requested child time, the largest overshoot, and the last publish. It states a cause only if the records show it.
81. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local failure records](target-local-failure-records.md) and [target-local status](target-local-status.md).
