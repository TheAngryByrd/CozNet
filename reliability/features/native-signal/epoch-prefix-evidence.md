# Completed epoch prefix evidence

Pausable collection now retains a disabled counter baseline for every successful source enable.
Successful completion emits the first source sample and enabled CPU prefix for each epoch.
This change does not alter sample credit or authoritative wall-time payment.

## Managed checks

The Release build and both host groups exit zero:

```powershell
dotnet build tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj -c Release --no-restore
dotnet tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll --verify-pausable-managed
dotnet tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll --verify-bound-samples-managed
```

The executable groups do not print individual check counts.

| Check | Required behavior |
| --- | --- |
| `DisabledBaselinesPreservePartialAndEmptyEpochs` | Prefixes use disabled readings, preserve empty epochs, accept zero deltas, and retain exact source indexes and samples. |
| `InvalidSampleIntervalsReject` | Missing counts, counter regression, out-of-range counts, missing epochs, and reordered epochs reject without changing valid inputs. |
| `InvalidSourceEpochsReject` | Missing, duplicate, reordered, foreign, and incomplete source evidence rejects. |
| `FailedEnableCannotProduceStart` | Failed enable cannot produce a start or advance the disabled session. |
| `OwnerDrainsPreserveExclusionAndEpochs` | Real collector integration retains three baselines and their first samples through final cleanup. |

The synthetic fixture places the first sample before the post-enable reading.
Its exact expected prefixes, `40`, null, and `10` ns, detect substitution of that later reading for the disabled baseline.

## Linux collection

The capture is `artifacts/epoch-prefix-collection/20261016T050433682-7bd46284`.

```powershell
pwsh -NoProfile -File native/VerifyBoundSamples.ps1 -EvidenceDirectory artifacts/epoch-prefix-collection
```

Both portable groups and all five live cases pass.
The cases are collection, pause/resume, invalidation before enable, invalidation during collection, and invalidation after close.
Input hashes remain unchanged.

The pause case records these source prefixes:

| Epoch | Disabled baseline, ns | First source index | Enabled prefix, ns |
| --- | ---: | ---: | ---: |
| 1 | 0 | 0 | 1005800 |
| 2 | 506841400 | 456 | 143700 |
| 3 | 948025900 | 871 | 935100 |

The resumed prefixes differ from a new nominal 1 ms period.
This observation is consistent with the retained timer remainder. It does not establish exact selected CPU attribution.

## Linux selected payment

The capture is `artifacts/epoch-prefix-payment/20261016T050549443-224121bb`.

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 `
  -EvidenceDirectory artifacts/epoch-prefix-payment -SelectedPayment -OwnerDrain `
  -DirectReceiptReady -FiniteMeasurement -SelectedCadence qualified-sample -SelectedBatches 50000
```

All three expected cases pass: zero speedup, 50-percent speedup, and invalidation after selected payment.
All four managed groups, sixteen native checks, and retained replay pass.
Input hashes remain unchanged. The selected-payment analyzer passes 91 checks.
The invalidation record retains a rejected collection with null validated prefixes.

Independent JSON checks compare each prefix with its first source sample, disabled baseline, and exact enable journal entry.
They recompute each counter difference and check source identity and baseline ordering.
The checks distinguish the frozen accepted prefix from the complete source stream.

| Case | Epochs | Source samples | Accepted samples | Accepted mapped baselines | Sum of mapped enabled prefixes, ns |
| --- | ---: | ---: | ---: | ---: | ---: |
| Zero | 1 | 458 | 456 | 0 | 0 |
| 50 percent | 130 | 417 | 415 | 61 | 59999000 |

The treatment's 61 mapped baseline records have 61,000,000 ns of nominal periods.
Their enabled prefixes range from 641,400 to 1,966,900 ns.
Counter increments can exceed a nominal period. They remain enabled CPU, not selected-method CPU.
These observations do not qualify a new credit policy or establish prediction accuracy.

## Accepted limits

Prefix parsing now adds a consistency gate to `Complete()` after source cleanup.
Invalid prefix evidence rejects collection instead of returning a successful result with partial diagnostics.
The live collection and sampled-payment checks exercise that gate without weakening accounting limits.

The source must start fresh and disabled with a zero counter, as the existing `ParseFresh` contract requires.
The explicit zero-baseline and counter-order guards include defense beyond states constructible through valid session transitions.
The prefix parser repeats final session closure validation to protect its own boundary.
The collector also retains its existing closure validation for the main result.
The tests reject malformed prefixes directly but do not inject a prefix-specific failure through `Complete()`.
Source inspection establishes cleanup before that parser call. The live invalidation case exercises a different rejection path.

Each enable adds one retained start object and a list entry.
Final parsing and serialization allocate after the frozen measurement endpoint.
No isolated overhead measurement or allocation-failure recovery claim is made.
Prefix parsing trusts the collector's owner checks, stopped producer, complete old-epoch drain, and CLR lifetime validation.
It does not infer those facts from a cumulative counter alone.
The retained Python analyzers do not yet require or replay the new optional fields.
The independent JSON check covers these live records, not a versioned offline evidence contract.

See [the reference](../../../docsSrc/Reference/bound-cpu-session.md#completed-epoch-prefixes)
and [the earlier sensitivity calculation](epoch-credit-sensitivity.md).
