# Managed cooperative boundary

`NativeCooperativeClient` uses source-generated imports and a controller-owned native session handle.
Both ABI layouts are checked at 88 bytes. The finalizer retains unresolved native ownership.
Successful submission records generation ownership before receipt-tracker allocation.

`CooperativeSite` and `CooperativeLeaseIdentity` parse identities before dependent operations.
The child representation carries explicit site fields. It does not reuse interrupted instruction addresses.
`CooperativeReceipts` copies raw input, preserves each child's first receipt bounds, and retains the first rejection permanently.
The receipt clock is read after native copy return in the same native nanosecond domain.
The 100 ms upper bound remains inclusive. Later reads can add children but cannot replace earlier bounds.

```csharp
var generation = client.Submit(0, command, budget);
var observation = client.Read(0);
var result = adapter.Accept(observation, state);
```

`CooperativeAccountingAdapter` returns immutable adapter/ledger updates.
It preserves native error, child, receipt, continuation, prefix, and aggregate checks.
All three site fields participate in eligibility. Ineligible children must contain no wait.
Replayed prefixes cannot pay twice. Concurrent selected credit survives settlement.
Cancellation requires managed intent. A valid terminal journal requires at least one child.

Cleanup copies and releases native journals independently of receipt acceptance.
It retains an immutable final header, child list, and status before release.
The owner continues polling through termination, then retires only after release. The controller closes only after owner join.
The interop fixture keeps unresolved owner state alive for process termination instead of forcing native close.
Five cases verify tracker reset and independent settlement on a second generation, including after receipt rejection.
The active-cleanup case enters cleanup with an unclaimed request and checks final cancelled-child retention.

```mermaid
flowchart LR
    Native[Native journal] --> Raw[Retained raw copy]
    Raw --> Receipt[First receipt bounds]
    Receipt --> Bind[Exact site and lease binding]
    Bind --> Ledger[Immutable ledger settlement]
    Raw --> Cleanup[Native cleanup without ledger acceptance]
```

Manual managed polls now have a client and ledger boundary.
Injected probe binding, actual CLR code lifetime, sampled capacity, and physical prediction remain incomplete.
See [the reference](../../docsSrc/Reference/cooperative-managed.md), [native interop](cooperative-interop.md), and [the lifecycle plan](../plans/experiment-lifecycle.md).
