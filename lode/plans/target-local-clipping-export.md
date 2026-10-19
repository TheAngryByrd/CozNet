# Target-local clipping export

## Supervisor decision

The supervisor approved this step on 2026-10-19, after it read `lode/tmp/target-local-clipping-review.md`:
- Add a bounded export after the measurement, complete both reviews, and then run exactly one new paired diagnostic on the reviewed tree.
- Clipping cannot explain the change from peer 8 to peer 2. No text may say that it does.
- The begin and end values of G cannot show the number of delay steps in a window. The export counts the steps, with the ordering and boundary rule of the estimator.
- The exact signed and clipped totals and `NegativeTransactions` cover all transactions, independently of the bounded window export. The export reports the total, exported, and omitted counts, and it selects windows deterministically.
- A nonpositive signed total gives an undefined diagnostic prediction.
- `estimatorAssumptionsValid` is reported separately from protocol validity and from the qualification criteria.
- The public estimator does not change. The export is diagnostic evidence, not public API.
- The original suites, the earlier paired run `20261019T213806586-069c2adf`, and its failed warm-up stay as they are.

## Limits of the estimator wording

- The trace of [the latency estimator plan](target-local-latency-estimator.md) shows an algebraic identity of signed measures: for complete transactions without clipping, `Σ v_i = ∫ N (dt - dG)`. It does not show a physical or causal latency for each transaction.
- `t - G(t)` jumps backward when G steps. So the value of one transaction can be negative, and only the sum has a meaning, and only while no value is clipped.
- The nominal sample period is a statistical credit. A step of 750 microseconds at 75% does not show that the last 1 ms of selected CPU belongs to the transaction whose window holds the step.

## Order of observations at the stamps

- The ledger stamps a publish with `CLOCK_MONOTONIC` after its compare-and-swap (`TargetDelayLedger.h:166`, `263`). `Stopwatch` reads the same clock on Linux.
- In one thread, `Begin` reads the clock after `post` returns. So a publish of that thread inside `post` has a time at or before b, and G(b) includes it. A publish at the same time as b counts in G(b), so it is not in the window.
- Across threads, the order of the compare-and-swap and of the stamps can differ. Another thread can raise G before b and stamp the publish after b. Then the window holds that step although G was raised before the window began. The journal cannot show this case.
- `catchUp` drains up to 64 pending samples of the calling thread before it settles (`TargetLocalApi.Linux.cpp:117-120`). Samples that the ring does not yet show, or samples of other threads, can publish later.

## Decision

- An internal function computes, from the journal and the transaction list after the experiment:
  - The publish times where the running maximum of G rises. Publishes with the same time form one step. A publish that does not raise the maximum is not a step.
  - For each transaction: b, e, G(b), G(e), the signed value `(e - b) - (G(e) - G(b))`, and the steps, which are the rise times t with `b < t <= e`. This is the boundary rule of `virtualLatency`.
  - For all transactions: their count, the signed total, the clipped amount `ClippedNs` (the sum of `-v_i` over negative values), the clipped total, `NegativeTransactions`, and a histogram of windows with 0, 1, 2, and 3 or more steps.
  - The first 4096 windows in the order of the transaction list, which is the order of `End`. The list itself has no enforced bound, so the count of omitted windows is reported.
- `TargetCausalProfile` gets an internal entry point that passes this evidence for each experiment to a callback. The public `run` and the estimator do not change. The sample becomes a friend assembly of `CozNet`.
- Only the mode `transaction-selected-profile` writes the evidence, to `<output>.evidence.json`. The records of the other modes do not change.
- The evidence is computed between experiments, on the calling thread. The profile forces a full collection before each experiment, so the temporary allocation is collected before the next timed span. The exported windows stay in memory until the profile ends. With 5 experiments of at most 4096 windows, that is a few megabytes at most. The new run is still not an exact repeat of the earlier run, and the report gives the collection counts.
- The analyzer checks the evidence of each complete profile record, also when a plain arm of the block failed. The unclipped difference needs a valid block, and the medians use only blocks that are valid, with valid evidence, for both peers.
- `native/AnalyzeTargetBottleneck.py` checks the evidence of each experiment, and reports:
  - `evidenceValid`, if the counts, the totals, and the identities match the profile record.
  - `estimatorAssumptionsValid`, false when an experiment has a negative transaction.
  - The unclipped difference, or none when the signed total is 0 or less.
  These do not change the block validity or the label of criterion 137.

## What the exact measurement can and cannot show

It can show, for this one run:
- The exact clipped amount of each experiment, and its exact effect on each diagnostic point.
- The number of steps in each window, by the rule of the estimator, and so whether the one-step bound of the review held.

It cannot show:
- A causal mapping of delay to transactions.
- Whether congestion causes the error of peer 8, or how the idle-time confound acts.
- Any result of another run, or of the qualification suites.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 139 and change none.

140. Unit tests of the evidence function cover mixed positive and negative transactions, a boundary at the same time as a step, overlapping windows, repeated publishes of the same maximum, more than one step in a window, and the bound of the export.
     They assert that the clipped total equals the signed total plus `ClippedNs`, that it equals `virtualLatency`, and that `ClippedNs` is 0 without publishes.
141. Unit checks of the analyzer show that an evidence record is invalid when a count or an identity does not match, that a baseline with `ClippedNs` above 0 is invalid, and that a signed total of 0 or less gives no unclipped prediction.
142. On the reviewed tree, the paired diagnostic runs exactly once. The run is retained, whatever its outcome, and the earlier paired run stays retained.
143. The report gives the label of criterion 137, the clipped and unclipped S8 and S2 over the common valid blocks, the number of undefined unclipped values, `ClippedNs` for each transaction at 75% for each peer, and the step histograms.
     It also states whether any window of the profile `selected` 75% experiments had 2 or more steps. A window with 2 or more steps means that the one-step bound of the review did not hold.
144. After the commit, every source hash in the new manifest equals the SHA-256 of the committed blob. No run is repeated to replace a failure.

See [target-local latency estimator](target-local-latency-estimator.md) and [target-local status](target-local-status.md).
