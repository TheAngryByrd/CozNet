# Matched controller ownership comparison

This diagnostic compares separate-controller and owner/controller execution at source `3842930bbd2a94475f7c61bf49ae92b18a3d6286`.
The retained root is `artifacts/owner-controller-comparison/20261016T040051329`.
`plan.json` declares the order and settings before execution.
`outcomes.json`, each runner summary, and `comparison.json` retain all outcomes.
`summarize.py` retains the aggregation code beside those records.

## Protocol

Four groups use separate/owner/owner/separate order.
Each group runs zero speedup, 50 percent, and invalidation after selected payment, in that order.
Each normal case requires 50,000 selected batches with `qualified-sample` cadence.
Selected payment, owner draining, direct receipt readiness, finite measurement, and pause timing are enabled.
Dispatch timing, scheduler capture, child CPU, peer progress, selected handoff, and batch service are disabled.

The debt and lease limits remain 2 ms. The child request limit remains 50 microseconds.
The handler limit remains 5 ms. The receipt limit remains 100 ms.
Each workload retains the 30-second process watchdog.
There were no retries or excluded arms.
This fixed order balances position between modes. It does not eliminate drift or establish statistical confidence.

Each group uses this command, with `-OwnerController` only for owner groups:

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 `
  -SelectedPayment -SelectedCadence qualified-sample -SelectedBatches 50000 `
  -OwnerDrain -DirectReceiptReady -FiniteMeasurement -PauseTiming `
  -EvidenceDirectory <group-directory>
```

## Integrity and outcomes

| Group | Capture directory | Expected checks |
| --- | --- | --- |
| 01 separate | `01-separate/20261016T040104506-40f1072e` | 3/3 |
| 02 owner | `02-owner/20261016T040147806-065ed0b7` | 3/3 |
| 03 owner | `03-owner/20261016T040228634-b14cb29f` | 3/3 |
| 04 separate | `04-separate/20261016T040314329-e695d1c8` | 3/3 |

All runners returned zero and retained unchanged inputs.
Source hashes, binary hashes by filename, and SDK image identity match across all four groups.
Each group passed the existing managed, native, outcome, and analyzer checks.
Every record passed evidence replay.
All twelve cases joined the peer and closed the native session without cleanup errors or receipt parser rejection.

Each invalidation case retains `The CLR probe lifetime is no longer admitted.`
Each has one selected payment and one incomplete pause. The incomplete pause has no complete duration.
These four expected failures remain invalid measurements.

## Treatment observations

All values in the following table are milliseconds, except counts.

| Group | Pauses | Total pause | Median pause | Publication to callback total | Median selected receipt | Maximum peer receipt |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 01 separate | 141 | 33.258080 | 0.180015 | 21.124971 | 0.113573 | 20.504020 |
| 02 owner | 156 | 21.053453 | 0.083742 | 4.857894 | 0.012260 | 14.683500 |
| 03 owner | 154 | 23.573216 | 0.087729 | 6.234626 | 0.012534 | 14.949722 |
| 04 separate | 154 | 40.158204 | 0.197350 | 26.803939 | 0.123344 | 16.860826 |

Pause duration uses `Timing.Completed - Timing.Started`.
Publication-to-callback duration uses `CallbackReturned - SamplesPublished`.
Receipt duration uses `ReceiptInterval.Observed - Started`.
Each conversion uses the record's timestamp frequency.
These intervals overlap. Their totals must not be added.
They include managed work and scheduling. They do not isolate native service cost or controller CPU.

| Group | Peer payments | Median peer receipt, ms | Maximum full peer interval, ms | Peak peer debt, ns | Accepted selected wait, ns |
| --- | ---: | ---: | ---: | ---: | ---: |
| 01 separate | 248 | 0.053342 | 22.878542 | 500,000 | 37,172 |
| 02 owner | 280 | 0.014822 | 16.968162 | 999,941 | 134,585 |
| 03 owner | 271 | 0.012623 | 16.803114 | 500,000 | 667,652 |
| 04 separate | 268 | 0.105508 | 18.938496 | 500,000 | 88,782 |

Full peer duration uses `Finished - Started` and includes release.
Each treatment freezes no pending peer request, active peer wait, or unsettled peer wait.
Frozen peer global and local values agree in every treatment.
Frozen selected debt is 71, 54, 91, and 126 ns, respectively.
No treatment reports a workload failure boundary.

## Elapsed variation and qualification

| Group | Zero-speedup elapsed interval, ms | Treatment elapsed interval, ms | Descriptive effective reduction |
| --- | ---: | ---: | ---: |
| 01 separate | 401.419–406.911 | 505.319–514.441 | -14.82% to -11.03% |
| 02 owner | 459.307–465.730 | 507.178–512.681 | +1.91% to +4.44% |
| 03 owner | 557.752–567.313 | 496.006–501.103 | +20.77% to +23.00% |
| 04 separate | 546.889–552.346 | 530.820–536.227 | +12.39% to +14.23% |

Effective treatment duration subtracts frozen global delay before comparison with its group's zero-speedup case.
These are descriptive arithmetic intervals, not qualified predictions or statistical confidence intervals.
The zero-speedup cases themselves vary substantially with unchanged inputs.
Accepted treatment sample counts are 446, 490, 470, and 465.
Selected credit is 53.5, 62.0, 58.5, and 57.0 ms.
Thus, identical fixed work did not produce identical sample or payment counts.

All normal measurements remain unqualified for source tail, sample-boundary membership, and instrumentation attribution.
Every treatment also retains an unqualified ledger residual.
The comparison has no matched physical optimization arm.
It cannot establish prediction accuracy or separate runtime variation from changes in sampled attribution.

## Decision

Keep owner/controller mode opt-in.
Both owner groups have shorter observed selected receipt and callback intervals than both separate groups.
This supports using the mode in further controlled experiments.
It does not establish general savings, stable capacity, or a prediction improvement.
No accounting bound or default changes follow from this comparison.
The next accuracy work must address measurement boundaries and calibrate the physical optimization.
Repeating this unchanged comparison cannot resolve those missing contracts.

See [the human explanation](../../../docsSrc/Explanations/cooperative-pause-cost.md)
and [managed integration evidence](managed-owner-controller.md).
