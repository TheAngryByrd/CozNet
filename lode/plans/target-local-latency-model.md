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
- Accounting: `simulate` raises an error unless the scheduled credit equals the samples times the nominal credit and equals the published credit. It also raises an error unless the published credit equals the skipped delay, plus the paid delay, plus the final debt.
- The estimators reject a warm-up tick outside the run, and they give no value when no transaction begins after it. The model stops with an error only after the longest possible drain.
- The domain is exact payment. Payment overshoot, scheduling contention, and the mix of sample phases in a real run are outside it.

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

See [target-local latency estimator](target-local-latency-estimator.md) and [target-local status](target-local-status.md).
