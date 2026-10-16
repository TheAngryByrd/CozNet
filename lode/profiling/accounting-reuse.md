# Immutable accounting reuse

`CooperativeAccountingCache` binds selected and peer handles in the controlled bound sampled workload.
The selected owner alone mutates the accounting variable and uses the cache.
No native state, receipt acceptance, source ownership, or CLR admission is cached.

```csharp
var prepared = accounting.PreparePeer(state);
state = prepared.State;
var debt = accounting.Observe(state);
```

`Observe` caches one successful `CooperativeDebtSnapshot` by exact `SignalAccounting` reference.
Different state objects require full capture even if debt numbers agree.
`PreparePeer` caches only the resulting state from a successful no-lease preparation.
It skips repeated preparation only for that exact resulting state.
A reservation clears the idle entry. Exceptions do not replace successful entries.
The F# accounting model remains immutable and does not change its behavior.

```mermaid
flowchart LR
    State[Immutable accounting state] --> Identity{Exact cached identity?}
    Identity -->|yes| Reuse[Reuse validated result]
    Identity -->|no| Full[Run capture or preparation]
    Full -->|success| Store[Store snapshot or idle result]
    Full -->|failure| Retain[Preserve prior successful entry]
```

Peer overshoot is published through the full catch-up operation before its idle result is cached.
Pending and settled service states are different objects, so their observations cannot reuse older operation data.
Fresh debt records retain current timestamps and enforce the unchanged 2 ms gate.
`BoundSampledPaymentWorkload.Debt` is a readonly value record with nine signed 64-bit fields.
Each observation still reads the current timestamp and accounting snapshot.
Nullable peak, final, and failure observations retain value copies without one heap object per observation.
`ObservePeaks` selects each debt maximum independently and retains the original timestamp on ties.
Its direct check covers initial absence, independent increases, decreases, and ties.
The JSON shape retains the same nine properties and null for an absent observation.
The calling-thread allocation check detects 880,000 bytes over 10,000 observations with the earlier reference record.
It requires zero bytes for the value-record construction and retention path.
This removes allocation, not the observation, native polling, or debt gate.
The [live integration](../../reliability/features/native-signal/debt-value-observations.md) passes zero, 50-percent, and expected-invalidation cases.
The final helper and allocation checks pass on Windows and Linux. Full-workload timing benefit remains unmeasured.
The debt error message is formatted only when the gate fails.
`CooperativeAccountingCacheChecks` covers reuse, new credit, pending and unsettled service, overshoot publication,
equal-debt distinct state, foreign-state rejection, and recovery of the previous valid cache entry.
Fresh idle and post-overshoot checks compare cached results with full model preparation and capture using structural equality.
They protect the required idempotence of idle preparation, including fields outside numeric debt.

The baseline component run `artifacts/accounting-cost/20261015T182853729` retains 672 bytes per debt capture,
328 bytes per idle preparation, and 1,000 bytes per combined call.
These isolated measurements do not establish their share of full profiler overhead.
The seven-stage run `artifacts/accounting-cost/20261015T183419066` measures zero allocation for repeated-state cache hits.
The same-process uncached combined stage retains 1,000 bytes per call.
The zero-speedup live reference capture retains a median paired ratio of 2.95 with all 16 records valid.
The live cadence matrix passes all twelve cases without changing a bound.
Continuous treatments still perform 19,173 and 10,504 pauses with only 2 and 12 mapped complete intervals.
This single successful matrix does not establish stable capacity or prediction accuracy.
See [the retained comparison](../../reliability/features/native-signal/accounting-reuse.md).
See [ledger debt](cooperative-debt.md), [observer overhead](observer-overhead.md),
[the reference](../../docsSrc/Reference/cooperative-managed.md), and [the index](../lode-map.md).
