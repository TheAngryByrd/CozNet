# Selected payment closure

`PaySelected` delegates admitted payment to `ExecuteSelectedPayment`.
The helper contains the captured callback and leaves the no-payment returns outside its allocation scope.
The payment body and all lifetime, source, debt, and receipt checks remain unchanged.

## Compiled path

Release IL before extraction starts `PaySelected` with `newobj` for its display class.
That allocation precedes `PollFromProbe` and the entry-site return.
After extraction, `PaySelected` begins with the poll arguments and call.
The display-class allocation belongs to `ExecuteSelectedPayment`.
Retained scratch inspection: `lode/tmp/probe-closure/before-il.txt` and `after-il.txt`.
IL inspection establishes compiler placement. It does not measure runtime allocation or elapsed savings.

## Entry callback allocation

Capture: `artifacts/probe-entry-cost/20261016T104143706`.
Four fresh Linux x64 processes run in before/after/after/before order on .NET 10.0.0.
Each process warms 2,000 calls, then measures 500,000 calls through the real compiled `PaySelected` entry-site callback.
The harness creates a real bound native client, registers its selected owner, and checks cleanup and completion.
Reflection prepares the callback before measurement. It does not run inside the measured loop.

| Case | Calling-thread allocation | Bytes per call |
| --- | ---: | ---: |
| Before 1 | 24,000,000 | 48 |
| After 1 | 0 | 0 |
| After 2 | 0 | 0 |
| Before 2 | 24,000,000 | 48 |

All processes exit zero and complete cleanup. Input hashes remain unchanged.
The capture retains source, runner, project, input hashes, logs, and raw records.
The baseline binary comes from the retained aggregate long comparison.
This experiment exercises the entry-site return only. It does not measure loop-site service, selected payment, or full-workload cost.
No timing threshold or claim follows from these four processes.

## Live integration

Capture: `artifacts/probe-closure-live/20261016T103945442-bd573a2a`.
The runner uses 50,000 selected batches, interval 8192, nominal-event credit, prepared receipts, owner draining, and owner/controller execution.
Finite measurement and direct receipt readiness remain enabled.
Zero speedup, half speedup, and expected invalidation after selected payment all pass.
The source, cooperative, pausable, and measurement-boundary preflight groups pass on Linux.
Replay passes. Input hashes remain unchanged.
This capture does not qualify prediction or measure whole-workload savings.

## Physical comparison

Capture: `artifacts/probe-closure-comparison/20261016T104424164-d8d7dd522fd848ccab14dae53bf00543`.
All four arms complete 500,000 selected batches with no invalid arms, integrity errors, or runner errors.
The sampled effective elapsed reduction is 39.2405–39.2551%.
The physical elapsed reduction is 52.9283–52.9295%.
The descriptive shortfall remains 13.6732–13.6890 percentage points.
Treatment consumes 2,396.9494 ms of selected CPU. Sampled zero consumes 2,255.4106 ms.

The runner exits 1 because prediction remains unqualified.
Ledger residuals, source-tail membership, sample boundaries, instrumentation attribution, calibration, and the single-block protocol remain qualification limits.
These ranges are descriptive boundary ranges, not confidence intervals or accuracy bounds.
The earlier long comparison uses a different execution and cannot isolate savings from this refactor.

## Review limits

Claude and Codex found no confirmed defects in the extraction.
The allocation evidence is not an automated regression gate.
A future compiler or capture change can restore allocation while functional checks remain green.
This coverage limit is accepted for the small refactor and its retained measured evidence.
The scratch harness depends on compiler-generated names and measures only the entry-site return.
The helper also adds a stack frame and can change first-use timing. No exact timing claim is made.

See [the human explanation](../../../docsSrc/Explanations/bound-observer-overhead.md#allocation-before-selected-payment).
