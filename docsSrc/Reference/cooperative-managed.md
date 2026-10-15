# Managed cooperative receipts and accounting

`NativeCooperativeClient` connects the internal .NET workload to `CozNet.Cooperative`.
It uses source-generated `LibraryImport` declarations, explicit C calling conventions, and a `SafeHandle`.
Both native records remain 88 bytes. Layout checks verify the final field offsets.
The public serial API does not use this client.

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
