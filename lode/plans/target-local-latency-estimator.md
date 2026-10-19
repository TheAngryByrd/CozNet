# Target-local latency estimator

## User decision

The user decided on 2026-10-19, after reading `lode/tmp/target-local-bottleneck-review.md`:
- A `ready` share of 10% is only a predeclared descriptive threshold. It does not prove that congestion is removed.
- The paired diagnostic uses only the `selected` points at 0, 25, 50, and 75%, with the plain `original` and optimized `selected` arms. It compares peer work 8 and 2 in the same blocks, on the same tree, with a fixed seed.
- Before a new live run, the plan documents the pinned upstream formula and compares it with CozNet on a hand-worked trace. A concrete defect has priority over the diagnostic. The estimator does not change only to fit measurements.
- The original qualification suites and their criteria do not change.

## Pinned upstream rule

The source is Coz `256e3c389424d6a1691ee5d18b5ecb5a6c1001ec`, cached under `lode/tmp/upstream-coz`.

- `latency_point` keeps two counters, begin and end, for the whole process (`libcoz_progress_point.h:99-183`).
- At the start of an experiment, the profiler saves both counters (`libcoz_profiler.cpp:307`, `libcoz_progress_point.h:152`).
- At the end, it logs three counts (`libcoz_profiler.cpp:438-441`):
  - `arrivals`, the begins during the experiment (`libcoz_progress_point.h:164`).
  - `departures`, the ends during the experiment (`libcoz_progress_point.h:168`).
  - `difference`, the begin count minus the end count at the end, which is the transactions in flight at that one instant since the process started (`libcoz_progress_point.h:172`).
- `duration` is the elapsed time minus the global delay added during the experiment, in nanoseconds (`libcoz_profiler.cpp:320-322`, `361-364`, `391`).
- The profiler logs an experiment only with at least 5 visits on each counter and a delay below the elapsed time (`libcoz_profiler.cpp:412`, `libcoz_profiler.h:48`).
- The viewer adds the arrivals and the durations of all experiments with the same line and speedup. It averages `difference` over them, weighted by duration (`viewer_ts_profile.ts:1063-1089`).
- The latency is `difference / (arrivals / duration)`, in nanoseconds of virtual time (`viewer_ts_profile.ts:868-871`). The speedup of a point is `(baseline - latency) / baseline` (`viewer_ts_profile.ts:1146`).

Assumptions of this rule:
- Occupancy is sampled once per experiment, at its end. The sample is a time average only over many experiments whose end instants fall at random points of the transaction cycle.
- The end of an experiment follows a fixed wall-clock wait. So the expected sample is the occupancy averaged over wall time, not over virtual time.
- The rule uses Little's law, so it assumes a steady state. Transactions that are open at the start or the end of an experiment enter only through the counts.

## CozNet rule

- Each experiment has its own `TargetProgressCounter`. The consumer drains the queue, so each begun transaction ends in the same experiment, and the arrivals equal the departures.
- The virtual latency of transaction i is `(e_i - b_i) - (G(e_i) - G(b_i))`, where G(t) is the largest published global delay at or before t (`TargetCausalProfile.virtualLatency`). A negative value counts as 0, and `NegativeTransactions` counts it. The prediction is `1 - (VL / V) / (VL0 / V0)`.
- Since [the progress settlement](target-local-progress-settlement.md), `Begin` and `End` settle the debt of the calling thread before they read the clock.
- The earlier CozNet rule was `W * E / T`, the mean wall latency times the effective time over the elapsed time ([target-local transactions](target-local-transactions.md)). [The transaction delay plan](target-local-transaction-delay.md) replaced it, because its `peer` predictions failed.

## Relation of the two rules

Let N(t) be the transactions in flight, A the arrivals, T the elapsed time, ΔG the global delay added, and D = T - ΔG.
- When each transaction begins and ends in the window, `Σ (e_i - b_i) = ∫ N dt` and `Σ (G(e_i) - G(b_i)) = ∫ N dG`. The identities below hold only when `NegativeTransactions` is 0. The run before the settlement has clipped transactions at `selected-75`.
- So the CozNet rule gives `VL / V = ∫ N (dt - dG) / A`. This is Little's law in virtual time, with the exact time average of the occupancy and no edge transactions.
  It is an algebraic identity of signed measures for complete transactions without clipped values, not a causal latency of each transaction. `t - G(t)` jumps backward when G steps, so one transaction can have a negative value. See [the clipping export plan](target-local-clipping-export.md).
- The expected upstream value, with the occupancy sampled at a random wall-clock instant, is `(∫ N dt / T) / (A / D)`. With every transaction in the window, this equals `W * D / T`, the earlier CozNet rule.
- The difference of the two rules is `(∫ N dG - N̄ ΔG) / A`, where N̄ is the wall-time mean occupancy. It is 0 when the delay rate is not correlated with the occupancy.
  The CozNet rule is lower than the upstream rule when delay is added at a high occupancy, and higher when delay is added at a low occupancy.

So the per-transaction rule is an intentional extension, not a port of the upstream estimator. The two agree when the covariance of the occupancy and the delay rate is 0.

## Hand-worked trace

The window is 0 to 100 ns. The global delay steps to 10 at t = 25 and to 30 at t = 60. A transaction that begins at the time of a step does not subtract that step, because G includes publishes at or before a time.

| Transaction | Begin | End | Wall | Delay added | Virtual |
| --- | ---: | ---: | ---: | ---: | ---: |
| T1 | 5 | 40 | 35 | 10 | 25 |
| T2 | 20 | 70 | 50 | 30 | 20 |
| T3 | 60 | 90 | 30 | 0 | 30 |
| T4 | 80 | open | | | |

- T1 and T2 overlap from 20 to 40, and T2 and T3 from 60 to 70. Each step crosses a begin or an end of a transaction. T4 is outstanding at the end.
- CozNet: T4 has no end, so the counter does not keep it. `virtualLatency` gives 75 ns for 3 transactions, 25 ns each, with no negative transaction.
- Exact virtual Little's law over the complete transactions: `∫ N dt = 115`, `∫ N dG = 2 × 10 + 1 × 20 = 40`, so `(115 - 40) / 3 = 25` ns. It equals CozNet.
- Earlier CozNet rule `W * D / T`: `(115 / 3) × 70 / 100 = 26.83` ns. The difference from 25 is `(40 - 1.15 × 30) / 3 = 1.83` ns, the term above.
- The upstream formula, evaluated on this trace: arrivals 4, departures 3, difference 1, D = 70, so `1 / (4 / 70) = 17.5` ns. This is one sample of the occupancy, at the end.
  Upstream would not log this experiment, because it has fewer than 5 visits on each counter. The value only shows the formula.
- Expected upstream value with T4: `∫ N dt = 135`, so `(135 / 100) / (4 / 70) = 23.63` ns.
- Exact virtual Little's law with T4: `(75 + 20) / 4 = 23.75` ns.

The trace shows no porting defect in `virtualLatency`. It gives the virtual-time identity for complete transactions without clipped values, and the outstanding transaction does not occur in the suites, because the consumer drains the queue. The identity does not show a causal latency.

## Comparison on the retained runs

These are the signed medians over the valid blocks, in points. "Upstream" is the earlier `W * E / T` rule, computed from `LatencyNs`, `EffectiveNs`, `ElapsedNs`, and `Visits` of the same experiments. "CozNet" is the per-transaction rule.

| Run | `selected-25` upstream / CozNet | `selected-50` | `selected-75` | `peer-25` | `peer-50` | `peer-75` |
| --- | --- | --- | --- | --- | --- | --- |
| `20261019T175316141-ec3885bd`, before the settlement | +4.0 / +4.3 | +7.1 / +6.4 | +9.7 / +4.6 | -0.1 / +3.2 | -5.3 / +0.1 | -5.8 / +1.6 |
| `20261019T183627302-c5843c4c` | +1.2 / +0.4 | +1.8 / -2.0 | +2.0 / -11.8 | -4.3 / -1.4 | -5.6 / +0.3 | -9.0 / +1.1 |
| `20261019T191349654-efea55a8` | -8.6 / -9.7 | +2.9 / -1.6 | +2.0 / -10.9 | -13.4 / -10.3 | -14.5 / -8.3 | -15.0 / -5.9 |
| `20261019T193820084-e379d1a4` | +2.5 / +1.4 | +2.4 / -1.5 | +3.7 / -10.5 | +0.8 / +3.9 | -4.3 / +0.7 | -8.7 / +1.0 |
| `20261019T195335859-ea098418` | -0.5 / -1.9 | -1.2 / -4.6 | -1.4 / -17.2 | -7.3 / -4.6 | -11.5 / -5.8 | -13.4 / -5.4 |
| `20261019T202333518-e70c6219` | +3.0 / +1.8 | +0.1 / -3.1 | +3.0 / -10.8 | -2.6 / +0.5 | -5.7 / +0.4 | -8.5 / +0.6 |
| `20261019T205349855-8c82f58d` | +3.0 / +2.3 | +2.7 / -2.0 | +1.4 / -12.5 | -4.8 / -1.9 | -6.8 / -0.5 | -5.6 / +3.4 |

- After the settlement, the upstream rule keeps `selected-75` between -1.4 and 3.7 points, but it misses the `peer` points by up to -15.0 points. The CozNet rule does the opposite.
- At `selected-75`, the windows capture 0.62 of their wall time as delay, against an overall rate of 0.73. That is the correlation term above. The delay steps fall at a lower occupancy than the average.
- These values describe the two rules on the same records. They do not show which rule is right, and the estimator does not change on this basis.

## Paired diagnostic

The comparison shows no concrete defect, so the paired diagnostic of the user decision runs next.

- `native/VerifyTargetLocalBottleneck.ps1` runs 5 blocks. Each block holds one profile and the plain `original`, `selected-25`, `selected-50`, and `selected-75` arms, for peer work 8 and for peer work 2.
- The 10 arms of a block run in an order from the fixed seed 20261018, as in the other suites. So the two peer values share each block, the tree, and the host state.
- The profile has 4 experiments, so the rotation is `(block - 1) % 4`. Block 5 repeats the order of block 1, and the positions are not balanced over the 5 blocks.
- The profile mode `transaction-selected-profile <peer> <seed> <rotation> <out>` profiles only `Work.selected` at 0, 25, 50, and 75%, after the same warm-up experiment. The plain arms use `plain-transaction`, with its matched warm-up and collection.
- Selected work stays 40 rounds, so the optimized arms have 30, 20, and 10 rounds for both peer values.
- `native/AnalyzeTargetBottleneck.py` checks each block and peer as the transaction suite does, and computes the points with the same functions. It writes `summary.json` in the run.
- The run is a diagnostic. It does not replace the transaction suite, and it does not qualify anything.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 133 and change none.

134. A unit test pins the hand-worked trace: `virtualLatency` gives 25, 20, and 30 ns for T1 to T3, with no negative transaction.
135. Unit checks of the diagnostic analyzer show that a block and peer is invalid when an arm is missing, fails, has no passed warm-up, or does not hold its mode, rounds, and duration, or when the profile does not hold the four experiments, its duration, and its period.
     They also show that the points use the per-transaction rule of `transaction-selected-profile`, and that a block without idle counts is invalid.
136. On the reviewed tree, the diagnostic runs once. The run is retained, whatever its outcome.
137. The outcome uses the signed median of `selected-75`, S8 for peer 8 and S2 for peer 2, and R2, the median `ready` share of the profile `selected` 75% experiments for peer 2.
     S8, S2, and R2 are medians over the blocks that are valid for both peers. At least 4 such blocks are needed, or the outcome is not decisive.
     - Congestion is supported if S8 is -8 points or lower, S2 is between -5 and 5 points, and R2 is at most 10%.
     - Estimator or boundary bias is supported if S8 and S2 are both -8 points or lower, and R2 is at most 10%.
     - Any other result is not decisive. If S8 is above -8 points, the earlier error did not repeat.
     These outcomes support a hypothesis. They do not prove it. The outcome is a label, not a pass rule, and the 10% limit of R2 is only descriptive.
138. The report gives, for each peer, the signed medians at 25, 50, and 75%, the window rate and the overall delay rate at 75%, the upstream-rule medians, and the idle classes of the plain `selected-75` arm.
     The idle class of the plain arm can change with peer 2, because its consumer idles longer, about 130 microseconds. This is a confound, and the report states it.
139. After the commit, every source hash in the new manifest equals the SHA-256 of the committed blob. No run is repeated to replace a failure.

See [target-local window payment](target-local-window-payment.md), [target-local wake after idle](target-local-wake-idle.md), and [target-local status](target-local-status.md).
