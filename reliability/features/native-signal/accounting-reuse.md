# Immutable accounting reuse

## Contract

The bound sampled workload uses a selected-owner cache for immutable debt snapshots and successful idle peer preparation.
The key is exact `SignalAccounting` reference identity. Numeric debt equality is insufficient.
New credit, service progress, settlement, and overshoot publication require full accounting operations.
Native polling, CLR admission, receipt checks, and source control remain outside this cache.
Debt records retain fresh timestamps. The 2 ms gate and its exception text remain unchanged.
The workload formats that exception text only on failure.

## Component measurement

The scratch driver constructs legal state through `SignalAccounting.create` and `register`.
It registers a sampled selected handle at zero percent and an unsampled peer with no debt or pending operation.
It calls the compiled workload through expression-compiled delegates, with fixture construction and reflection outside measurement.
Each stage runs 2,000 warmup calls, followed by 12 shuffled blocks of 10,000 calls per stage.
Seed: `20261015`. Platform: Linux x64, .NET 10.0.0, with tiered compilation and ReadyToRun disabled.
The driver checks zero debt and absence of reservations outside each measured bracket.
It unwraps preparation results without boxing them in the measured path.

Initial four-stage run: `artifacts/accounting-cost/20261015T182853729`.
It retains 48 rows and allocates 672 bytes per capture, 328 bytes per idle preparation, and 1,000 bytes per combined call.

Seven-stage comparison: `artifacts/accounting-cost/20261015T183419066`.
It retains 84 rows, with cached and uncached calls measured in the same process.
The cache stages use the exact resulting idle state after setup.
They measure repeated-state hits, not first calls or state changes.

| Stage | Mean ns per call | Bytes per call |
| --- | ---: | ---: |
| Control | 1.170 | 0 |
| Debt capture | 356.681 | 672 |
| Idle preparation | 146.113 | 328 |
| Idle preparation and capture | 498.006 | 1,000 |
| Cached debt capture | 2.418 | 0 |
| Cached idle preparation | 3.194 | 0 |
| Cached idle preparation and capture | 3.791 | 0 |

Both runs exit zero and retain unchanged source and binary hashes.
All rows remain in the results. Control costs are not subtracted.
These measurements show removal of repeated-state allocation.
They do not establish a latency bound or the component's exact share of live profiler cost.
The initial four-stage and later seven-stage drivers differ, so their timing means are not an isolated before/after comparison.

## Zero-speedup live comparison

Run: `artifacts/fixed-work-observer/20261015T183439106`.
It uses the same reference driver, balanced shuffled order, image, and fixed 5,000 selected batches as the earlier observer capture.
All 16 processes complete with matching checksums and joined workers.
All eight profile-zero records pass the retained source replay with no payments or pauses.
Input hashes remain unchanged within the capture.

| Block | Reference ms | Profile-zero ms | Ratio |
| ---: | ---: | ---: | ---: |
| 0 | 32.917 | 55.654 | 1.691 |
| 1 | 15.614 | 61.460 | 3.936 |
| 2 | 19.326 | 50.299 | 2.603 |
| 3 | 18.367 | 58.177 | 3.168 |
| 4 | 19.650 | 53.706 | 2.733 |
| 5 | 17.122 | 63.323 | 3.698 |
| 6 | 22.931 | 55.422 | 2.417 |
| 7 | 17.572 | 116.587 | 6.635 |

Median paired ratio: `2.950316931365348`, compared with `7.37493320636678` in the earlier capture.
All rows, including the slower reference in block 0 and profile in block 7, remain included.
The captures are separate sequences, and their reference timings differ.
They do not isolate the cache's exact elapsed-time effect from scheduling, code generation, or deferred message formatting.
The reference uses a separate outer loop and a delegate call, while the profile rewrites the source method before JIT compilation.
Peer totals cover different lifetimes. The pair order remains grouped and can align with time drift.
These are diagnostic ratios, not general application overhead estimates.

## Live selected-payment matrix

Run: `artifacts/bound-selected-cadence/20261015T183603714-21ff53d4`.
All twelve records pass runtime outcomes and evidence replay.
Each child retains unchanged inputs and passing native, managed, outcome, and Python checks.
The full target exits zero. All owners join, native ownership closes, and cleanup reports no errors.

| Order and mode | Treatment selected work ms | Pauses | Mapped complete intervals |
| --- | ---: | ---: | ---: |
| 0: continuous | 3,101.027 | 19,173 | 2 |
| 1: qualified | 109.875 | 12 | 15 |
| 2: qualified | 158.635 | 14 | 9 |
| 3: continuous | 2,231.913 | 10,504 | 12 |

Each completed treatment executes 5,000 selected batches.
The 2 ms debt, 50 microsecond child request, 5 ms handler, and 100 ms receipt limits remain unchanged.
Earlier failed runs remain retained. One passing matrix does not establish stable capacity.
Continuous mode still spends substantial time pausing and excludes most selected sampling opportunities.
Physical prediction accuracy remains unverified.

## Contract checks

`CooperativeAccountingCacheChecks` runs through `CooperativeDebtChecks` in both cooperative managed groups.
The final bound managed group passes on Windows and Linux.
The live captures precede the final test-only model-equivalence assertions.
The final portable groups use the revised test binary. Runtime implementation sources are unchanged from those captures.

| Check | Evidence |
| --- | --- |
| Repeated-state identity and no reservation | `IdleResultAndSnapshotAreReused` |
| New credit, pending operation, unsettled wait, settlement, and overshoot | `CreditAndPendingServiceInvalidateReuse` |
| Equal debt in a different state | `EqualDebtInAnotherStateRequiresFreshObservation` |
| Foreign-state rejection and valid-entry recovery | `RejectedStateDoesNotPoisonReuse` |
| Null state before the first successful entry | `NullStateCannotMatchAnEmptyCache` |

Fresh idle and post-overshoot checks also compare reused results with full preparation and capture by structural equality.
This checks all immutable fields and the model's required idle idempotence.
These portable checks do not directly exercise the workload's private timestamp and failure-publication ordering.
That integration remains covered by source review and retained live records, not a focused deterministic observation test.

The runtime cache remains confined to the selected owner. It has no concurrent-access contract.
Raw captures and scratch measurement drivers remain in ignored local artifact directories, outside repository clones.

See [the reference](../../../docsSrc/Reference/cooperative-managed.md),
[observer overhead](../../../docsSrc/Explanations/bound-observer-overhead.md), and
[the original observer comparison](fixed-work-observer.md).
