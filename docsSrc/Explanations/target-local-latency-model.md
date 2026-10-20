# Target-local latency model

The latency estimator of a CozNet causal profile has no closed form for a queue between two threads. A small deterministic model shows how the estimators behave in that case. The model is in `native/TargetLatencyModel.py`, and `native/TargetLatencyModelChecks.py` checks it.

The model is a simplified model of the transaction workload. It is not a CozNet execution, and its results are observations of finite runs at a stated tick resolution. They are not requirements for profiler correctness.

## What the model contains

- A producer runs `p` ticks of selected work for each transaction and puts the transaction in a queue of `K` items. A consumer takes it, runs `c` ticks, and ends it.
- A virtual speedup `s` adds delay credit `s T` for each `T` ticks of producer work. This is the generated credit of the producer. The model keeps it separate from the published global delay G.
- A publication policy sets when the generated credit becomes part of G:
  - `immediate` publishes each sample at the tick boundary that ends the tick of the sample, as each CozNet sample dispatch publishes at once. Its credit can be split into parts that are published at different ticks, and the parts must add up to `s T`.
  - `release` publishes the generated credit only when the producer enqueues an item, before the item takes its token. This is a controlled policy, not upstream Coz. Upstream also publishes at a 10 ms timer, and the model has no timer.
- Generated credit that is not yet published is unpublished credit. Each snapshot reports it. A run that completes has none.
- The consumer pays its debt before its work and before the end of the transaction. When it was blocked, it skips its debt to the release token of the item that it takes.
- The producer never pays, because it is the sampled thread. This is true only while each payment is exact. In the profiler, a payment can overshoot, and then another thread can publish above the producer. The model does not contain that case.
- A real optimization is a separate run with `p (1 - s)` ticks of selected work and no delay.

All values are whole ticks. The model rejects an input whose real work or credit is not a whole number of ticks, and it rejects malformed inputs.

## Order of events

At each tick boundary:

1. Credit that is due raises the global delay G. G is the running maximum of the published values.
2. The producer and the consumer act until neither can act again at that instant. So a space that the consumer frees and a producer that waits for it meet at the same instant.
3. A snapshot counts each begin and each end at or before that instant.

## The estimators

- The clipped rule is the CozNet estimator: the mean over the transactions of their wall time minus the delay added in their window, with a negative value counted as 0.
- The signed rule is the same mean without the clip. It is a diagnostic. A signed total of 0 or less gives no prediction.
- The upstream rule is the rule of Coz `256e3c389424d6a1691ee5d18b5ecb5a6c1001ec`: the in-flight count at the stop, divided by the arrival rate over the effective duration. The model forms its counters from the snapshots at the warm-up tick and the stop tick.
- The model can also apply the upstream rule to earlier snapshots of one continuing run. Such a snapshot is not an earlier stop: when a begin falls at the same instant, the continuing run counts it and a stopped run does not.

## What the checked cases show

- With fine credit, where `T` is shorter than a transaction, the clipped rule stays near the real gain. In the checked transition case, the clipped gains are -0.674 to -0.652 against a real gain of -0.714.
- In the checked coarse-credit case, where `T` is longer than a transaction, the result depends on the sample phase. In 96 of its 160 phases the consumer pays nothing:
  - In some phases, each delay step is published while the consumer is blocked. The release token carries the step, the consumer pays nothing, and the profile wall time stays at its unloaded value.
  - In other phases, a step is published while the consumer works. The consumer then pays the whole step, and the queue waits behind it.
- Splitting a credit into parts changes the values of single windows, but it does not change the credit.
- In the checked phase cases (p 40, c 16, K 4, s 3/4) with `release` publication, the consumer pays nothing in any of the 160 coarse phases or the 16 fine phases. The profile wall time stays at its unloaded value.
  The clipped gain is 0.25 in every coarse phase and 15/28 in every fine phase, against a real gain of -5/7. So, without a timer, deferred publication removes the payment of these phase cases.
  In the 96 coarse phases without payment under `immediate`, both policies give 0.25, so the errors are equal. In the other 64 coarse phases and in all 16 fine phases, `immediate` is closer to the real gain. The `release` policy is not the upstream implementation, and it is not a proposed fix.
- Hand-derived checks show where the two policies differ. A consumer that pays a published step before it waits skips nothing at the wake. With `release` publication, the same consumer skips the step. A consumer that finds an item already in the queue skips nothing with either policy, and it pays the published step. When a full queue holds the producer, the consumer pays with both policies.
- In the five named hand-derived cases, both policies have the same producer work and generate the same credit. This is not guaranteed for an arbitrary run of fixed duration, because a full queue can change how much work the producer does before the stop.
- Every run must publish all of its generated credit before it completes.
- In the checked cases, doubling every time value doubles every time in the run and keeps each checked gain within 1e-12. These cases do not show convergence in general.

## Limits

- The model does not show the cause of a measured prediction error, and it does not establish an estimator fix.
- The producer of the model repeats its period exactly, so the phase of each sample repeats. A real producer drifts, and the model does not give the mix of phases in a real run.
- The `release` policy has no timer, and the consumer settlement stays at its CozNet places for both policies. So neither policy is a model of upstream Coz.
- A payment before a wait can change whether the consumer blocks and which token it receives. So a change of the settlement place can change the total payment. An earlier audit claim that the settlement place keeps the total payment is withdrawn.
- With `release`, a publish happens during the actions of an instant. A stamp at the same instant reads G after all actions of that instant, so a window that ends there includes a publish that came after its end in the action order.
  So a single window is not a trace of the order of source events.
- Payment overshoot is a restriction of the model. The retained live records do not bound it. An earlier audit bound of 189 µs is withdrawn: the payment and skip of the producer do not bound a rise of G from another thread.
- The runs before the boundary export do not record the counts at the stop, the stop time, or G at the stop. So the upstream rule cannot be applied to them.
- The boundary export records upstream-like ordered reads, not a common-instant snapshot. In its one diagnostic capture, the stop occupancy is 1 or 2 in each experiment.
  So one stop sample can change the upstream value of an experiment by a factor of 2. A few such samples cannot establish a stationary latency estimate.

See [target-local prediction](target-local-prediction.md) and the Lode plan `lode/plans/target-local-latency-model.md`.
