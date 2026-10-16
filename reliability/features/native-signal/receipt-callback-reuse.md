# Receipt callback reuse

The bound workload creates three readiness delegates during setup.
Each delegate reads the current client when invoked. Readiness results are not cached.
Peer visibility remains `== 3`. Selected visibility remains `>= 3`.
Native readiness, call counts, controller ownership, dispatch timing, and debt checks retain their existing paths.

## Allocation evidence

Capture: `artifacts/receipt-callback-allocation/20261016T122804812`.
Baseline: the retained workload from matched capture `20261016T120710656-f42b4a42dc9a404cac32cf1baa851b6a`.

The scratch harness extracts the compiled `Service` instructions from loading `receiptReadiness` through calling `Ready`.
It emits those instructions into a separate dynamic method before measurement.
The baseline segment contains two delegate constructors. The changed segment loads setup fields instead.
The changed fixture initializes those fields before measurement. It does not execute the workload's full setup or service method.
Both segments call the real direct readiness method against a submitted, unserviced native request.
The peer remains available for explicit cancellation and cleanup after measurement.

Four fresh Linux x64 processes run in before/after/after/before order on .NET 10.0.0.
Each process warms 2,000 calls, then measures 500,000 calls.

| Case | Calling-thread allocation | Bytes per measured call |
| --- | ---: | ---: |
| Before 1 | 64,000,000 | 128 |
| After 1 | 0 | 0 |
| After 2 | 0 | 0 |
| Before 2 | 64,000,000 | 128 |

Every process records 502,000 direct checks and completes cancellation, release, target retirement, and worker join.
All four exit zero. Input hashes remain unchanged.
The capture retains the harness, runner, project, input hashes, raw records, and logs.

Two earlier fixture attempts remain retained:

- `20261016T122553919` queried readiness without an outstanding generation.
- `20261016T122716199` attempted target retirement before releasing the cancelled request.

Both failed in all four cases. The final fixture fixes those setup and cleanup defects.
Neither failed capture supplies a completed allocation result.

This measures the extracted readiness segment, not the complete `Service` method.
Controller fallback still constructs its inner dispatch callback.
The result does not establish allocation-free fallback, whole-workload savings, stable capacity, or prediction accuracy.

## Live direct mode

Capture: `artifacts/receipt-callback-live-direct/20261016T122839250-25661b31`.

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 -EvidenceDirectory artifacts/receipt-callback-live-direct -SelectedPayment -OwnerDrain -SelectedCadence qualified-sample -SelectedBatches 50000 -ProbeInterval 8192 -PrepareReceipts -OwnerController -NominalSampleCredit -FiniteMeasurement -DirectReceiptReady
```

Zero speedup, half speedup, and expected invalidation after selected payment pass.
The four managed preflight groups and retained replay pass. Inputs remain unchanged.
No payment bound changes. This capture does not qualify prediction.

## Live controller fallback

Capture: `artifacts/receipt-callback-live-controller/20261016T123019162-3f2622cb`.
The command uses the same settings as the direct capture, but omits `-DirectReceiptReady`.
Zero speedup and expected invalidation pass. Treatment fails after 43,959 batches at 2,910,302 ns selected debt.
The limit remains 2,000,000 ns. The runner and replay exit 1, preserving the workload failure.

The treatment retains 276 peer payments and 49 selected payments before failure.
Its last peer request has a 382,596 ns budget and records 3,792,898 ns of measured wait.
The last child requests 50,000 ns and records 3,519,019 ns of wait.
Publishing measured peer overshoot raises global delay to 117,045,907 ns, above the selected local value of 114,135,605 ns.
The selected-debt gate rejects that difference. The records do not identify why the child wait ran long.

The direct-readiness counter remains zero, confirming controller fallback selection.
All four managed preflight groups pass. Inputs remain unchanged, workers join, and cleanup reports no errors.
The fallback path reaches both receipt types, but the complete treatment does not pass capacity.
No unchanged timing retry replaces this failure. The separate direct capture does not isolate the cause of the fallback failure.

## Matched physical comparison

Capture: `artifacts/receipt-callback-comparison/20261016T123610158-dbe4980fb9e84ced914b7e5587aa6ba8`.
The declared block uses 500,000 batches, interval 8192, prepared receipts, owner/controller mode, nominal credit, selected CPU, and sample-clock capture.
All four arms complete with exit zero. Analysis reports no invalid arms, integrity errors, or runner errors.
Input hashes remain unchanged. Both sampled clocks are consistent.

Sampled effective elapsed reduction is 37.0712–37.1088%. Physical reduction is 47.1064–47.1076%.
The descriptive shortfall is 9.9977–10.0364 percentage points.
Selected CPU is 2,160.2897 ms at zero speedup and 2,353.8143 ms in treatment, a difference of 193.5246 ms.
This separate block does not isolate callback reuse or establish improvement over previous captures.
The runner exits 1 because prediction remains unqualified.

The retained analyzer produces membership reports in `artifacts/receipt-callback-membership/20261016T124045539`.
Both comparison domains pass. All 1,988 selected treatment samples are source-inside and accepted.
All 994,000,000 ns of treatment credit is source-inside. Unreceived events and exact CPU attribution remain unbounded.
Frozen ownership, ledger residuals, instrumentation attribution, calibration, and the single-block protocol still prevent qualification.

## Review limits

Compiled IL and the allocation fixture detect the removed construction.
The existing behavioral checks do not enforce an allocation threshold.
The fixture depends on compiler-generated method names and extracts only one call segment.
These limits remain separate from live integration coverage.

See [the explanation](../../../docsSrc/Explanations/bound-observer-overhead.md#receipt-callbacks-during-pending-service).
