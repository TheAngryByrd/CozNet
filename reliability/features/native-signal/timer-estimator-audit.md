# Retained timer estimator audit

Audit: `artifacts/timer-estimator-audit/20261016T124139832024`.
The retained script reads all eight 50-percent cases from `artifacts/matched-prediction/20261015T013834940-6d02c69e`.
It also reads `sampled-50.json` from `artifacts/timer-sampled/20261015T025206017-a2af01e3`.
No capture is repeated or changed.

## Replay scope

The audit checks Boolean acceptance, positive integer epochs, fixed 1 ms periods, and nonnegative integer credit.
Accepted epochs cannot decrease. It replays each recorded credit under the current interval rule.
The replay total must equal both the retained credit and the frozen endpoint's credit.
All nine cases pass these accounting checks. Input hashes remain unchanged.

The separate nominal total weights each accepted selected event by 50% of its period.
It is an alternate weight over the same retained observations, not a replacement for recorded credit.
It does not simulate changes to cadence, sampling pauses, scheduling, or payment.

| Case | Completed workload | Selected first events excluded | Recorded credit (ms) | Alternate nominal weight (ms) |
| --- | --- | ---: | ---: | ---: |
| Matched block 1 | No | 7 | 129.5 | 133.0 |
| Matched block 2 | Yes | 27 | 940.5 | 954.0 |
| Matched block 3 | Yes | 23 | 937.0 | 948.5 |
| Matched block 4 | No | 1 | 27.5 | 28.0 |
| Matched block 5 | No | 8 | 200.0 | 204.0 |
| Matched block 6 | No | 3 | 119.5 | 121.0 |
| Matched block 7 | No | 1 | 2.5 | 3.0 |
| Matched block 8 | Yes | 36 | 939.5 | 957.5 |
| Counter capture | No | 7 | 196.0 | 199.5 |

All six failed workloads retain their failure text. No prediction aggregate or completed-subset estimate is produced.
The audit does not replay source counters, validate native journals, or establish source-time membership.
The old matched records do not contain the newer counter evidence.
Input hashes establish the files used; they do not establish measurement validity.

## Decision

The timer backend still uses an estimator that excludes first events, while the bound cooperative backend has an opt-in nominal-event path.
Implement the same nominal-event option for timer experiments before comparing the two intervention mechanisms.
Keep interval mode for compatibility. Bind the new policy to source periods, cadence, metadata, and offline replay.
Do not infer that the alternate weights will fix the retained capacity failures or prediction error.

See [nominal-event semantics](../../../docsSrc/Explanations/nominal-event-credit.md)
and [the original matched evidence](matched-prediction.md).
