# Managed cooperative receipts and accounting

`NativeCooperativeClient` connects the internal .NET workload to `CozNet.Cooperative`.
It uses source-generated `LibraryImport` declarations, explicit C calling conventions, and a `SafeHandle`.
Both native records remain 88 bytes. Layout checks verify the final field offsets.
The public serial API does not use this client.

## Optional direct receipt readiness

`NativeCooperativeClient.ReceiptReady(slot)` queries the outstanding generation through the native readiness API.
The import uses the existing `SafeHandle`, an explicit C calling convention, and an unsigned integer output.
The client accepts only `0` and `1`. Native errors and invalid output values throw.
Managed callers must serialize submit, query, and release operations. The native any-thread query does not make this client thread-safe.

`COZNET_DIRECT_RECEIPT_READY=1` enables direct queries in normal selected and peer polling.
The runner exposes this option as `VerifyBoundSampledPayment.ps1 -SelectedPayment -DirectReceiptReady`.
The option requires selected payment. An absent value or `0` preserves controller header polling. Other values reject configuration.
Cleanup, full receipt copies, validation, accounting, and release still use their existing controller paths.
All debt and timing limits remain unchanged.
Mailbox quiescence can require another poll after a controller snapshot could report retirement.
Direct query failures throw on the polling thread. Controller query failures retain the worker's aggregate exception wrapper.

Results record `directReceiptReadyEnabled` and `receiptReadyCalls`.
The count includes attempted direct queries, including queries that throw. Disabled mode records zero calls.
Replay checks activation and counts. With dispatch timing enabled, retained normal header dispatches invalidate direct-mode evidence.
Replay cannot reconstruct evicted dispatches or prove that every query used the intended slot.
The runner requires recorded activation to match its option.

The query uses portable atomic state and the existing native lifetime guard.
The sampled workload remains Linux-only. Capacity, overhead, and prediction benefits remain unverified.
See [the native readiness contract](cooperative-interop.md#receipt-readiness).
The [polling comparison](../Explanations/receipt-readiness.md) explains why controller polling remains the default.

## Identity and receipt tracking

`CooperativeSite.Parse` requires nonzero worker, code-version, and site identities.
`CooperativeLeaseIdentity.Parse` requires a generation from 1 through `UInt32.MaxValue`, a nonzero command, and a budget from 1 through 2,000,000 ns.
These values identify the declared site and request. They do not prove CLR probe origin.

`CooperativeReceipts` retains raw snapshots before parsing them.
It copies child buffers into read-only collections. Later caller changes cannot alter retained evidence.
For each new child, it retains the first native capture time and first managed receipt upper bound.
The client obtains that upper bound by reading the native clock after the snapshot call returns.
The same clock domain supplies native child timestamps.

The first upper bound minus the child's armed time must not exceed 100,000,000 ns.
The exact limit is accepted. A later read cannot replace or invalidate an already accepted first bound.
New children receive their own first bounds.
Unknown states, changed prefixes, shrinking journals, regressing clocks, failed API calls, and failed receipt clocks reject the capture.
`FirstFailure` retains the original failed capture. Later valid reads cannot repair it.
`LastRaw` continues to retain the most recent attempted capture.

Receipt acceptance is separate from payment acceptance. A copied failed child can remain raw evidence without qualifying for ledger settlement.

## Ledger binding

`CooperativeAccountingBinding` binds a ledger lease, generation, command, and exact cooperative site.
The child sequence combines the generation and ordinal without truncation.
The adapter checks all three site fields. A mismatched site can only defer with zero wait.

`CooperativeAccountingAdapter.Accept` returns a new adapter and ledger state.
It checks native errors, raw-record equality, contiguous ordinals, immutable prefixes, receipt bounds, continuation order, and aggregate payment.
It also preserves the 50,000 ns child request limit and 5,000,000 ns measured child-duration limit.
The ledger checks each child request against the remaining lease budget.
Replayed prefixes do not pay debt twice. Concurrent sample credit remains in the ledger after settlement.

Cancellation requires managed intent before a cancelled journal can settle.
Code revocation also requires cancellation intent for every affected outstanding lease before its cancelled journal can settle.
Interrupted waits retain valid measured payment. Deferred sites do not pay debt.
Every valid terminal cooperative journal must contain a child.
Failed evidence leaves the caller's existing ledger state and reservation intact.

## Immutable accounting reuse

The controlled bound sampled workload uses `CooperativeAccountingCache` on its selected owner thread.
The cache binds one selected handle and one distinct peer handle.
It reuses a debt snapshot only when the caller supplies the same immutable `SignalAccounting` object.
Another object requires full capture, even when its numeric debt is equal.

`PreparePeer` retains the resulting state only after successful preparation returns no lease.
A later call with that exact state returns the idle result without repeating preparation.
Changed state requires full preparation. A reservation clears the idle entry.
Failed capture or preparation leaves the last successful entry intact.
Peer overshoot must pass through catch-up before its resulting idle state can be reused.
Idle reuse requires full preparation to remain idempotent on that resulting state.

The cache is not thread-safe. The workload keeps accounting mutation and cache access on the selected owner.
Debt records still receive new timestamps, and every observation still enforces the 2 ms debt limit.
The cache does not skip native polling, receipt checks, CLR lifetime checks, or source ownership checks.

## Ownership and cleanup

The controller owns submission, snapshots, receipt tracking, cancellation, release, and close.
Each owner thread registers, polls, and retires its own native target.
Successful native submission records ownership before the client constructs its receipt tracker.
The client retains the generation if a later managed operation fails.

Cleanup copies the complete retired journal before native release.
`LastCleanup` retains immutable child records, the header, and native status before release.
Cleanup does not accept rejected receipts or settle the ledger.
The owner must continue polling until pending work terminates and the controller releases it.
The owner then retires. The host joins it before closing the session.
The handle finalizer does not free unresolved native ownership from the finalizer thread.

## Verification limits

The controlled interop workload uses manual polls on a dedicated managed thread.
Its cases cover paid, deferred, cancelled, late-receipt, wrong-thread clock, pending-cleanup, and diagnostic outcomes.
The diagnostic case captures active and retired native records without changing receipt tracking.
It verifies that later native reuse cannot change the copied diagnostic children.
Every case except pending cleanup also verifies a second independent lease on the same native slot.
The new tracker must not retain the previous generation's bounds or failure.
The previous failed ledger reservation remains pending.
Each case runs in a separate process because the native ABI permits one successful session per process.
The runner retains every case and uses a process watchdog.

The optional `injected-paid` case connects `CpuWorkload.Probe` to the native client through the existing thread-local callback.
It checks intervals 1, 64, 256, and 1024 in separate processes.
The callback skips the entry probe and services the lease at the second probe, inside the loop.
It verifies the checksum, exact helper count, payment, ledger settlement, and payment on a second generation.
The runner disables tiered compilation and ReadyToRun for these controlled processes.
The site still carries synthetic identities. The callback does not prove CLR code-version identity or prevent code replacement.

These checks do not pin CLR code versions, drive sampled debt, or establish prediction accuracy.
`NativeCooperativeClient.CaptureDiagnostic` returns raw native status and clock evidence without receipt parsing or ledger settlement.
The caller retains request ownership. A later release must take its own retired copy.
`BoundCooperativeClient` exposes this diagnostic after CLR rejection because capture does not accept the rejected binding.
No diagnostic record can restore lifetime admission or validate payment.
See [the native ABI](cooperative-interop.md), [the verification procedure](../How_Tos/verify-cooperative-interop.md),
and [the contract evidence](../../reliability/features/native-signal/cooperative-managed.md).

## Rejected managed receipt intervals

The bound sampled workload reports receiptIntervalCaptureVersion as 1.
receiptIntervalRejection is null unless CooperativeReceiptInterval.Parse rejects an interval.
A rejection retains the exact Started, Observed, and TimestampFrequency arguments.
Its Request retains Worker, Command, Generation, and Budget.
Worker 1 identifies the selected slot. Worker 2 identifies the peer slot.
These values are raw failure evidence. They do not authorize payment.

CooperativeReceiptIntervalCapture preserves the first parser exception and rejects later recovery attempts.
The workload parses the interval before it assigns the returned accounting state or appends accepted payment.
Successful intervals retain the existing ReceiptInterval contract.
Earlier request watchdog checks do not produce this parser-rejection record.

Replay binds each rejection to its failure boundary and original failure.
The rejection, result, and failure capture must use the same positive timestamp frequency.
The rejected endpoint must precede or equal the first failure capture, which captures the peer slot.
It rejects a valid interval reported as failed, mismatched identity, or the same request in accepted payments.
Legacy records without the capture version remain readable.
The current runner requires capture version 1 in every result.

Controlled checks cover the capture helper and replay rules.
They do not force rejection through either workload call site or verify its ledger assignment order at runtime.
The retained live captures contain no parser rejection.

## Controller dispatch timing

`COZNET_DISPATCH_TIMING` accepts `0` or `1`. An absent value disables timing.
The runner exposes `-DispatchTiming`, which requires `-SelectedPayment`.
The shared observer uses managed synchronization and `Stopwatch`. The live sampled command requires the Linux CPU source.

`dispatchTimingEnabled` records the activation Boolean. `dispatchTiming` is null when disabled.
Enabled evidence contains `Calls`, `Failed`, `Incomplete`, `Recent`, `Longest`, `LongestQueue`, `LongestAction`, and `LongestReturn`.
`Recent` retains at most 128 immutable records in sequence order.
The four maxima retain records independently of recent-history eviction. Ties retain the first record.

Each observation contains `Sequence`, `Operation`, `Command`, `Generation`, `Submitted`, `Started`, `Finished`, `Returned`, and `Failed`.
All timestamps use the result's `timestampFrequency`.

| Interval | Calculation | Meaning |
| --- | --- | --- |
| Queue | `Started - Submitted` | Caller submission through controller action entry |
| Action | `Finished - Started` | Controller action bracket |
| Return | `Returned - Finished` | Action exit through caller observation |
| Total | `Returned - Submitted` | Full dispatch bracket |

`Started` and `Finished` can be null after dispatch failure.
A late callback cannot rewrite the captured failure. Missing endpoints do not authorize a phase duration.
Action failures retain `Finished`. `Failed` describes the caller's dispatch outcome.
Submit records have generation zero until the native call returns. Session-wide operations have zero command and generation.

Operations identify session creation, selected or peer submit/header/read/release, failure capture, cleanup header/release, cleanup start, and close.
The existing 100 ms receipt checks remain separate from these diagnostic brackets.
Timing preserves the existing worker exception shape and does not authorize payment.

The observer adds clock reads, locks, and allocations. Its durations include that cost and exception handling.
The return bracket also includes worker fault publication. It is not a timestamp at task completion.
`Returned` precedes history and maximum updates. The total bracket excludes that final observer work.
They do not establish pure scheduler latency or CPU consumption.
Replay checks retained ordering, counts, and maxima. It cannot reconstruct evicted calls or verify maxima against their missing records.
Totals include unique observations retained as maxima, even when those observations have left `Recent`.
Replay does not bind diagnostic command and generation fields to accepted payments or failure boundaries.
Frequent header polls can evict earlier submit records. Maxima cover session creation and cleanup as well as payment.
Legacy records without both dispatch fields remain readable. The runner requires the requested activation mode in new results.
