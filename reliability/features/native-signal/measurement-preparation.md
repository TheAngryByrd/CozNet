# Measurement preparation evidence

The implementation uses source base `afb330660360e416e18762456ff355d66b2534a1` plus the retained preparation changes.
The final comparison snapshots the executed sources and binaries.
Preparation runs the real capture and validation path before the actual measurement.
It retains separate boundaries without using preparation progress or elapsed time in the estimator.

## First-call diagnostic

The declared diagnostic is `artifacts/physical-boundary-reuse/20261016T041259360221`.
Its small driver invokes the existing physical command four times in each process.
Separate baseline and optimized processes run on Windows and Linux, for sixteen records.
Every invocation completes 50,000 selected batches, passes the physical parser, and joins its peer.
Input hashes stay unchanged.
The Windows records identify .NET 10.0.13. Linux records identify .NET 10.0.0.
Each process has a 30-second watchdog. No failed or timed-out invocation was retried.

| Platform and mode | First start bracket | Later start brackets |
| --- | ---: | ---: |
| Windows baseline | 6.0559 ms | 0.4–0.6 microseconds |
| Windows optimized | 5.8010 ms | 0.4–9.4 microseconds |
| Linux baseline | 69.078863 ms | 0.403–1.004 microseconds |
| Linux optimized | 48.582891 ms | 0.453–0.758 microseconds |

These observations support preparation before measurement.
They do not identify JIT compilation as the sole cause of first-call cost.
A full earlier invocation also changes caches, runtime state, and scheduling.

## Prepared integration

The prepared physical capture is `artifacts/physical-boundary-prepared/20261016T042226122`.
It uses the same driver protocol with the changed implementation.
All sixteen physical records pass existing replay and explicit preparation checks.
Each preparation completes before the actual start, uses the same clock frequency, and contains zero selected progress or credit.
Peer progress can advance during preparation. The measured window starts from the later progress observation.

The prepared sampled plan is `artifacts/measurement-preparation-sampled/20261016T042501958`.
It runs both controller modes at zero speedup, 50 percent, and expected invalidation after selected payment.
All six expected checks pass with unchanged inputs, successful peer join, and native close.
The two invalidation cases retain invalid measurements.

| Controller mode | Capture | Zero start bracket | Treatment start bracket | Treatment end bracket |
| --- | --- | ---: | ---: | ---: |
| Separate | `20261016T042505946-bc7e82ea` | 15.650 microseconds | 17.517 microseconds | 865.352 microseconds |
| Owner | `20261016T042553542-55bd8ea6` | 16.637 microseconds | 28.285 microseconds | 177.949 microseconds |

These captures precede the final constructor-failure and early timestamp guards.
Those guards reject states that the captures did not reach.
The final comparison below executes the corrected implementation.
Preparation reads empty initial ownership. It does not exercise every pending-service branch of a later end capture.

## Final-code comparison

The capture is `artifacts/cooperative-prepared-comparison/20261016T042823543-df736d0be9444b59a0d4e6f421bcf548`.
The command is:

```powershell
pwsh -NoProfile -File native/VerifyCooperativeComparison.ps1 `
  -EvidenceDirectory artifacts/cooperative-prepared-comparison -SelectedBatches 50000
```

The runner returned 1 because prediction qualification remains unmet.
All four workload processes returned zero and completed their declared work.
The collection is complete, with zero invalid arms, no integrity errors, and no runner errors.
Original input hashes remained unchanged.
The retained analyzer passed 22 comparison checks and 91 selected-payment checks.
All sixteen native checks and the three managed check groups passed.

| Arm | Preparation start bracket, ms | Actual start bracket, microseconds | Actual end bracket, microseconds |
| --- | ---: | ---: | ---: |
| Physical baseline | 103.591576 | 42.675 | 0.737 |
| Physical optimized | 61.332411 | 12.479 | 0.619 |
| Sampled zero | 8.277136 | 29.445 | 8.701 |
| Sampled half | 6.615103 | 29.259 | 138.340 |

Every final window records `PreparationAttempted=true`, a complete preparation, and a later actual start.
The nested preparation records no further preparation attempt.
The sampled arms use the default separate controller and no pause timing observer.
All accounting bounds remain unchanged.

The descriptive sampled reduction is 8.7092%–8.7518%.
The physical reduction is 51.3344%–51.3554%.
Their descriptive difference is approximately minus 42.6 percentage points.
These are arithmetic intervals, not qualified predictions or statistical confidence intervals.
Narrower boundary brackets do not resolve the observed mismatch.
Source membership, instrumentation attribution, and treatment ledger residuals remain unqualified.

## Calibration scope

The earlier interleaved calibration uses the same `OriginalChecksumArithmetic` and `OptimizedChecksumArithmetic` source hashes.
The `Run` and `RunOptimized` method source is also unchanged.
Its estimate remains useful evidence about the selected arithmetic.
Its loop excludes cooperative progress publication and does not establish the exact reduction for the current instrumented program.
The comparison therefore retains `local-optimization-not-calibrated` and makes no new calibration qualification claim.
See [interleaved calibration](interleaved-calibration.md) for its clock and overhead assumptions.

## Regression checks and limits

| Check | Required behavior |
| --- | --- |
| `PreparationPreservesTheRealWindow` | Separate timestamps and accounting, one real release, and unchanged preparation data. |
| `PreparationCannotRepeatOrFollowStart` | Invalid sequencing and null readers reject before effects. |
| `FailedPreparationBlocksMeasurement` | Both accounting failures preserve exception identity and block reuse. |
| `PreparationClockMustMatchAndPrecedeMeasurement` | Frequency changes and overlap reject. Exact endpoint equality passes. |
| `PreparationConstructorFailurePreventsReuse` | Zero or throwing frequency getters retain the attempt and prevent recovery through a restored clock. |
| `PreparationFailureRemainsACaptureFailure` | The finite wrapper preserves the first capture failure and rejects preparation after closure. |

The Release build and `--verify-measurement-boundaries` pass on the host.
The final comparison executes that same check group in Linux.
No delivered test emits diagnostic output.

Preparation remains optional in the shared capture class.
The workload call sites use it, but current replay does not require or validate preparation records.
Removal of a call site therefore lacks an automated replay failure. The live records establish that the current callers execute preparation.
No live preparation fault was injected through workload cleanup.
Preparation adds accounting reads, clock reads, allocations, and peer execution before measurement.
The retained intervals are observations, not worst-case cost bounds or evidence of unchanged scheduling.
These accepted limits do not permit a prediction-accuracy claim.

See [the capture reference](../../../docsSrc/Reference/measurement-boundaries.md)
and [the comparison explanation](../../../docsSrc/Explanations/cooperative-physical-comparison.md).
