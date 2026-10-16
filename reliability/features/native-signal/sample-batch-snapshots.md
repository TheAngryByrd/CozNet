# Sample batch snapshot reuse

The pausable collector reuses frozen snapshots for unchanged source batches.
This removes repeated symbol copies on cache hits. It does not establish a full-workload performance benefit or prediction accuracy.

## Contract checks

The Release build and both host commands exit zero:

```powershell
dotnet build tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj -c Release --no-restore
dotnet tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll --verify-bound-samples-managed
dotnet tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll --verify-pausable-managed
```

These executable check groups do not print individual check counts.

| Check | Required behavior |
| --- | --- |
| `UnchangedBatchReusesFrozenSnapshotWithoutAllocation` | Empty and nonempty cache hits retain the frozen reference and allocate zero bytes over 10,000 warmed calls. |
| `MutableSymbolsCannotChangeEarlierSnapshots` | Replacement, shrink, empty, and growth preserve previous snapshots. Published lists reject mutation. |
| `SymbolOrderAndCaseRequireFreshSnapshots` | Reversal and case-only changes publish exact new contents while preserving previous contents. |
| `ChangedMetadataCannotReuseAnEarlierSnapshot` | A new source object publishes changed counters, loss, and throttling. |
| `SnapshotFailurePreservesThePriorEntry` | Comparison, copy, and argument failures preserve the successful cache entry. |
| `OwnerDrainsReuseStableSnapshots` | The owner drain reuses `Latest` and preserves final sample delivery and cleanup. |

## Retained comparison

The declared plan is `artifacts/sample-batch-cache/20261016T044531867/plan.json`.
The capture is `artifacts/sample-batch-cache/20261016T044531867/20261016T044539201-e5eeebcff8a943f3aa458744ac8b68b1`.
It uses source base `66d17c20d33f86adb67a5cf7b979dffff249b285` with the cache changes and 50,000 batches per arm.
The plan declares one block, no retries, and no isolated performance claim.
The later order and case assertions do not change the implementation used in this capture.

Both managed collection groups pass in Linux before the comparison.
All four workload processes exit zero. The collection is complete, with zero invalid arms and no integrity or runner errors.
The original input hashes remain unchanged.
The runner exits 1 because prediction qualification remains unmet.

| Arm | Descriptive cost per progress unit, ns |
| --- | ---: |
| Physical baseline | 3182.79238–3183.06494 |
| Physical optimized | 1576.50968–1576.77104 |
| Sampled zero | 10086.40578–10088.15672 |
| Sampled half | 9747.48280–9752.75274 |

The descriptive sampled reduction is 3.3079%–3.3770%.
The physical reduction is 50.4595%–50.4720%.
These are arithmetic intervals, not confidence intervals or qualified accuracy bounds.
This block is not paired with the earlier preparation block. Their difference cannot isolate the cache effect.

The zero-speedup arm accepts 494 samples, including 198 mapped samples and 296 unmapped samples.
The treatment accepts 438 samples, including 124 credited mapped samples, 61 uncredited mapped samples, and 253 unmapped samples.
The treatment records 62,000,000 ns of credit, 157,242 ns of selected payment, and 137 pauses.
Its actual start and end brackets are 30.226 and 233.271 microseconds.
The zero-speedup brackets are 71.294 and 16.253 microseconds.

Both sampled arms retain source-tail, boundary-membership, and instrumentation-attribution qualification failures.
The treatment also retains an unqualified ledger residual.
The comparison retains `local-optimization-not-calibrated` and `single-declared-block`.
All accounting bounds remain unchanged.

## Accepted limits

One serialized collector drain owns the cache. Concurrent source-list mutation during capture is unsupported.
Cache hits require the same source object and equal ordered symbols, with ordinal comparison.
Sources that return new batch objects still require a copy.
The cache retains one source object and one frozen snapshot.
Hits read symbol counts and indexed values. Those reads can throw, and failures preserve the prior entry.
The allocation check measures warmed calls on the current runtime and calling thread.
The live capture does not count cache hits or isolate their timing benefit.
The explicit null-symbol guard provides a clearer argument error. The copy operation would also reject a null collection without that guard.
The metadata check changes the source object. It does not test same-object scalar mutation, which init-only properties prevent.

See [the batch reference](../../../docsSrc/Reference/bound-samples.md#pausable-batch-snapshots)
and [the prepared comparison](measurement-preparation.md).
