# First-request timing at interval 8192

Capture: `artifacts/sparse-first-receipt-timing/20261016T063803753-1784ad28`.

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 `
  -ProbeInterval 8192 -EvidenceDirectory artifacts/sparse-first-receipt-timing `
  -SelectedPayment -OwnerDrain -DirectReceiptReady -FiniteMeasurement `
  -PauseTiming -DispatchTiming -SelectedCadence qualified-sample -SelectedBatches 50000
```

The runner exits zero. All three expected outcomes pass, with unchanged inputs and no cleanup errors.
Zero and 50-percent cases complete. Intended lifetime invalidation produces an invalid measurement, as expected.
Both normal measurements remain unqualified. Prediction accuracy remains unestablished.
This capture does not replace the [four-arm comparison](sparse-probe-comparison.md).

## Managed brackets

The treatment records 239 peer payments. Their median request bracket is 0.718808 ms.
The first request has command 1 and generation 1.
Its `Started` is 480605323005972 and its `Finished` is 480605344748769.
The managed timestamp frequency is 1,000,000,000 Hz.
Their difference is 21.742797 ms.
The receipt's managed `Observed` endpoint is 480605341851609.
These request brackets overlap selected work and do not measure selected-thread overhead.

The dispatch observer records 810 calls, zero failures, and zero incomplete calls.
It retains 128 recent records, from sequence 683 through sequence 810.
None retains command 1. All four independent maxima retain sequence 1, operation `Create`.

| Session creation bracket | Duration, ms |
| --- | ---: |
| Queue | 4.305076 |
| Action | 36.158529 |
| Return | 2.941987 |
| Total | 43.405592 |

Creation returns at managed timestamp 480605177615773.
Measurement starts within managed timestamps 480605297926109 and 480605297959039.
Thus, the maximum call occurs before measurement and cannot explain the first request's component costs.

## Versioned prefix capture

Follow-up capture: `artifacts/sparse-first-dispatch-prefix/20261016T064731331-623e8ada`.
It uses the same command options with a new evidence directory and the versioned dispatch prefix implementation.
All three expected outcomes pass. Input hashes remain unchanged within this capture.
The normal measurements remain unqualified.
This is a diagnostic follow-up, not a matched performance comparison with the earlier capture.

The treatment records 759 dispatches, no failures, and no incomplete calls.
Its prefix contains exactly 32 records. Sequences 2, 3, and 4 retain peer command 1.
The first request's managed bracket lasts 22.820403 ms.

| Operation | Queue, ms | Action, ms | Return, ms |
| --- | ---: | ---: | ---: |
| Peer submit | 0.974917 | 1.422004 | 0.415960 |
| Peer read | 1.186422 | 10.731190 | 0.649879 |
| Peer release | 0.373059 | 0.777448 | 0.091038 |

The read action runs from managed timestamp 481167392448780 to 481167403179970.
It contains native receipt reading and managed accounting acceptance.
Its wall-time bracket includes possible descheduling and does not isolate either component's CPU cost.
The three dispatch brackets exclude work between calls. They do not partition the entire request bracket.

The receipt records seven native children with 500,057 ns of accepted measured wait.
That value does not explain the managed read action's duration.
No subtraction between native and managed timestamps contributes to the table.

## Evidence limits and next diagnostic

First-request submit, read, and release records have left the bounded history.
Neither the recent tail nor setup maxima can reconstruct them.
The versioned prefix capture supplies those records for the follow-up request.
A controlled component diagnostic can now separate receipt observation and accounting acceptance within the read action.
Neither component is established as the cause by this capture.

`NativeCooperativeClient.Read` obtains the receipt upper bound through the native clock.
`CooperativeReceipts.Observe` retains this value as `FirstManagedReceiptUpper`.
Its name does not make it a `Stopwatch` timestamp.
Absolute native and managed timestamps cannot be subtracted without a verified clock mapping.
No JIT, GC, scheduling, or component CPU attribution follows from these brackets.

See [the explanation](../../../docsSrc/Explanations/cooperative-pause-cost.md#first-request-evidence-at-interval-8192)
and [the dispatch contract](../../../docsSrc/Reference/cooperative-managed.md#controller-dispatch-timing).
