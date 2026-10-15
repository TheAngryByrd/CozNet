# Batch validation preparation

`BoundSampleCollection.StartCore` calls `PrepareValidation` under its initial lifetime guard, before source creation.
The setup list is local and read-only. It exercises mapped and unmapped samples, duplicate IPs, symbol enumeration, counters, and period aggregation.
It does not use `Capture.Publish`, open admission, create an event, or update accounting.
Actual pause and completion validation remains unchanged.
Preparation moves first-use work into setup but does not guarantee a fixed validation duration.

## Isolated cost

Smoke capture: `artifacts/batch-validation-cost/20261015T192929882`.
Eight-process capture: `artifacts/batch-validation-cost/20261015T192940590`.
All eight cases passed with unchanged inputs.
The manifest retains source and binary hashes and Linux SDK image identity.
Each process uses .NET 10, disables tiering and ReadyToRun, and disables the profiler.
The retained input is the first 47-sample final-drain prefix from the failed phase capture.
The driver calls the real private `ValidateBatch` through a compiled delegate.
It reconstructs a matching aggregate batch from that prefix.
The snapshot diagnostic reproduces `GetRange`, `ToArray`, and the read-only wrapper without the capture lock.
First snapshot, first validation, repeated validation, and repeated snapshot always run in that order.
Each repeated stage calls its operation 10,000 times.

| Stage | Median | Minimum | Maximum | Calling-thread allocation per call |
| --- | ---: | ---: | ---: | ---: |
| First snapshot | 54.109 microseconds | 47.464 microseconds | 183.951 microseconds | 856 bytes |
| First validation | 27.495 ms | 18.436 ms | 35.499 ms | 6,048 or 18,384 bytes |
| Repeated validation | 3.459 microseconds | 3.154 microseconds | 4.264 microseconds | 4,480 bytes |
| Repeated snapshot | 0.143 microseconds | 0.134 microseconds | 0.206 microseconds | 856 bytes |

All observations remain in the report. This diagnostic does not isolate JIT CPU from scheduling or wrapper invocation.

## Prepared live capture

Capture: `artifacts/bound-pause-prepared/20261015T193222031-90cbc6a8`.

```powershell
./native/VerifyBoundSampledPayment.ps1 -SelectedPayment -PauseTiming -SelectedCadence qualified-sample -SelectedBatches 50000 -EvidenceDirectory artifacts/bound-pause-prepared
```

The zero-speedup and expected-invalidation cases passed.
The treatment failed after 5,187 batches with `The selected probe rejected cooperative service.`
All three records passed evidence replay, which preserved the failure.
Input hashes remained unchanged. Native checks, three Linux managed groups, and analyzer checks passed.
The corresponding three managed groups also passed on Windows.

The first treatment pause took 6.663734 ms.
Its drain-acknowledged to batch-validated interval took 0.399631 ms.
The previous unprepared capture recorded 42.732674 ms and 35.566524 ms respectively.
These are separate captures, not an isolated estimate of the change's exact effect.

Selected request 16 retained a 597,493 ns budget.
Its third 50,000 ns child ran from 440647125841761 to 440647132133778 on the native clock.
The 6,292,017 ns handler interval exceeded the unchanged 5 ms bound.
The raw child remains invalid and the header retains the native failure.
The run does not establish stable capacity or physical prediction accuracy.
No unchanged failure was rerun to obtain a passing result.

| Contract | Check |
| --- | --- |
| Setup publishes no samples and leaves initial count zero | `FinalDrainPreservesEpochAndCallbackBoundary` |
| Inconsistent real batches still reject | `InconsistentPausePreventsCallback` |
| Later loss invalidates earlier provisional evidence | `LateLossRejectsEarlierEpochs` |
| Timing preserves failures | Existing phase replay and managed failure checks |

See [phase evidence](bound-coverage.md#phase-timing) and [observer explanation](../../../docsSrc/Explanations/bound-observer-overhead.md).
