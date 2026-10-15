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
Its cases cover paid, deferred, cancelled, late-receipt, wrong-thread clock, and pending-cleanup outcomes.
The first five cases also verify a second independent lease on the same native slot.
The new tracker must not retain the previous generation's bounds or failure.
The previous failed ledger reservation remains pending.
Each case runs in a separate process because the native ABI permits one successful session per process.
The runner retains every case and uses a process watchdog.

These checks do not bind injected probes, pin CLR code versions, drive sampled debt, or establish prediction accuracy.
See [the native ABI](cooperative-interop.md), [the verification procedure](../How_Tos/verify-cooperative-interop.md),
and [the contract evidence](../../reliability/features/native-signal/cooperative-managed.md).
