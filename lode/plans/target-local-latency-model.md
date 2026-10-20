# Target-local latency model

## Supervisor decision

The supervisor approved on 2026-10-19 a permanent model and check patch, its documentation, and its reviews.
- The approval covers development. The numbers of the scratch revisions are not validated results.
- The production estimator, the exports, the payment, the protocol, and live captures do not change. No capture runs for this patch.
- The model contract must resolve the stop counters, the undefined values, the resolution claim, the input validation, and the credit accounting before the patch can pass review.

## Decision

- `native/TargetLatencyModel.py` holds the model, and `native/TargetLatencyModelChecks.py` holds its fixed-input checks. The build target that runs the analysis checks runs them too.
- The explanation is [the latency model page](../../docsSrc/Explanations/target-local-latency-model.md). The scratch design note is `lode/tmp/target-local-estimator-contract-review.md`.
- Stop counters: a snapshot at an instant counts each begin and each end at or before it, after all actions of that instant.
  So arrivals are the begins after the warm-up snapshot up to the stop snapshot. A begin at the warm-up tick is not an arrival, and an end at the stop tick has left.
  The difference is the absolute begins minus the absolute ends at the stop.
- Earlier snapshots of one run are observations of a continuing run. They are not independently stopped experiments, and a check shows a case where the two differ by one begin.
- Same-instant actions run to a fixed point. Without it, a freed queue space reached the waiting producer one tick late, and the real gain changed with the tick resolution: -0.696 at one resolution and -0.705 at twice that resolution.
  With it, doubling every time value keeps every checked gain within 1e-12, and the real gain of the transition case is -5/7.
- Undefined values: an upstream latency needs arrivals and a duration above 0. A gain needs a baseline above 0 and a treatment of at least 0.
  An upstream treatment of 0 is valid, and a signed total of 0 or less is not. The mean and the viewer aggregate over snapshots use the same admissible snapshots and report the excluded count.
- Accounting: `simulate` raises an error unless the generated credit equals the samples times the nominal credit and no generated credit is unpublished when the run completes. It also raises an error unless the published credit equals the skipped delay, plus the paid delay, plus the final debt.
- The estimators reject a warm-up tick outside the run, and they give no value when no transaction begins after it. The model stops with an error only after the longest possible drain.
- The domain is exact payment. Payment overshoot, scheduling contention, a publication timer, and the mix of sample phases in a real run are outside it.

## What changed from the scratch revisions

- The real gain of the coarse transition case is -5/7 (-0.714), not -0.696 or -0.705. The difference came from the missing fixed point.
- The single tie phases of revision 2 came from the same artifact, and they are gone. The coarse case now has 96 phases without payment and 64 phases with payment. By phase number, they form 4 runs of 24 phases without payment and 4 runs of 16 with payment.
  The 64 paid phases take 8 distinct wall times, each shared by 8 phases.
- With fine credit, the clipped gains of the transition case are -0.674 to -0.652 against the real -0.714.
- The live count of revision 1 is named the long-window share. It is a threshold on wall times, not a measure of payment, and its correlation with the error stays exploratory.
- No result of this model establishes a live causal mechanism or an estimator fix.

## Acceptance criteria

These criteria are fixed before the reviews. They add to criteria 1 to 149 and change none.

150. The checks reject malformed inputs: work, consumer work, queue, period, phase, stop, and speedup outside their ranges, inexact work or credit, parts and offsets of different lengths, negative parts or offsets, and parts that do not sum to the credit.
151. The checks assert the hand-derived traces: the baseline trace, the split and unsplit credit traces, and the same-instant queue handoff.
152. The checks assert the stop counters at the warm-up and stop ticks, the stop snapshot of a run, the difference between a continuing run and a stopped run, and the rules for undefined and zero gains.
153. The checks assert the credit accounting, one coarse phase without payment and one with payment, the doubling of every time value, and the fine-credit range.
154. Only deterministic checks run. No live capture runs.

## Publication policy

The supervisor approved on 2026-10-19 a deterministic extension of the model for the publication policy. It follows the intervention fidelity audit, the scratch note `lode/tmp/target-local-intervention-fidelity-review.md`.
- No change to the production intervention, the estimator, or the exports. No live capture.
- The audit trace was revised first. Revision 1 was not an executable schedule. Revision 2 uses the elapsed payment, the queue state, and the order of the checks.
- The audit withdrew its 189 µs overshoot bound and its claim that the CAS and `fetch_add` difference is minor. Overshoot stays a restriction of the model.
  The payment and skip of the producer do not bound a rise of G from another thread, because selected credit that the producer holds can cover it. A bound needs the producer selected credit, final local delay, final debt, unpublished credit at the close, and completion. The retained records do not hold them.
  The consumer payment plus skip equals G, but this shows only that the final local delay of the consumer equals G. It does not show which thread made each rise.
- The revised trace also withdrew the claim that the settlement place keeps the total payment. A payment before a wait can change whether the thread blocks and which token it sees.

Decision:
- `simulate` takes `publication`, `immediate` or `release`. `immediate` is the earlier model, and it stays the default. `release` publishes the generated credit at each enqueue, before the token. Credit parts apply only to `immediate`.
- `release` is a controlled policy, not upstream Coz, because it has no 10 ms timer. The consumer settlement stays the same in both policies, so this comparison changes one rule.
- The accounting keeps `generated`, the credit of the samples, separate from `published`, the rises of G. Each snapshot holds the unpublished credit, `generated - published` at that instant. A run that completes must have 0 unpublished credit, and the delay check stays `published = skipped + paid + finalDebt`.
- The earlier key `scheduled` is now `generated`, and each snapshot has a fifth value.

Hand-derived cases, with p 10, s 1/2, T 10, and phase 3, so that samples at CPU 3, 13, and 23 each generate 5:

| Case | c, K, stop | `immediate`: skipped, paid | `release`: skipped, paid |
| --- | --- | --- | --- |
| The consumer pays a published step before it waits | 4, 4, 15 | 5, 5 | 10, 0 |
| An enqueue while the consumer pays | 6, 4, 15 | 5, 5 | 10, 0 |
| An item already in the queue | 12, 4, 15 | 5, 5 | 5, 5 |
| A full queue holds the producer | 25, 1, 25 | 5, 10 | 5, 10 |
| Stop and drain | 4, 4, 25 | 5, 10 | 15, 0 |

- With `release`, a publish happens during the actions of an instant, and a stamp at that instant reads G after all of its actions. In the full-queue case, the window that ends at tick 40 includes the publish at tick 40, which comes after the end in the action order. So a single window is not a trace of source event order.
- In the full-queue case, `release` publishes the third sample at the enqueue at tick 40, not when the work ends at tick 30. The snapshots hold 5 of unpublished credit from tick 23 to tick 39.
- In the stop case, the stop snapshot at tick 25 holds 5 of unpublished credit with `release`, and the final enqueue at tick 30 publishes it.
- Both policies have the same begins, producer work, samples, and generated credit in each of these five cases. This is checked only for the named cases. It is not guaranteed for an arbitrary run of fixed duration, because a full queue can change the producer work before the stop.

Phase results for the checked transition case (p 40, c 16, K 4, s 3/4, stop 2000, warm-up 200), with a real gain of -5/7:

| Credit | Policy | Phases without payment | Clipped gain |
| --- | --- | ---: | --- |
| Coarse, T 160 | `immediate` | 96 of 160 | 0.25 in the 96 phases without payment, 8 values from -23/21 to -17/21 in the others |
| Coarse, T 160 | `release` | 160 of 160 | 0.25 in every phase |
| Fine, T 16 | `immediate` | 0 of 16 | -0.674 to -0.652 |
| Fine, T 16 | `release` | 16 of 16 | 15/28 in every phase |

- With `release`, each window holds the credit of its own producer work. That credit is published at its enqueue while the consumer waits, so the consumer skips it, and the wall time stays 56.
  Coarse: one window in four subtracts 120 and is clipped to 0, so the mean is 42 and the gain is 1/4. Fine: each window subtracts 30 on average, which gives a mean of 26 and a gain of 15/28.
- So, in these cases, deferred publication without a timer removes the payment. In the 96 coarse phases without payment under `immediate`, both policies give 0.25, so the errors are equal. In the other 64 coarse phases and in all 16 fine phases, `immediate` is closer to the real gain. This is an observation of the model, not a test oracle and not evidence about live runs. The `release` policy is not the upstream implementation and not a proposed fix.

Acceptance criteria, fixed before the reviews. They add to criteria 1 to 163 and change none:

164. The checks reject an unknown publication policy, and credit parts with `release`.
165. The checks assert the five hand-derived cases under both policies: the transactions, the publishes, and the generated, published, skipped, paid, final debt, and unpublished credit.
166. The checks assert unpublished credit at the stated snapshots, and the same begins, producer work, samples, and generated credit under both policies.
167. The checks assert the `release` phase results of the coarse and fine cases. The earlier checks of `immediate` do not change.
168. Only deterministic checks run. No live capture runs.

See [target-local latency estimator](target-local-latency-estimator.md) and [target-local status](target-local-status.md).
