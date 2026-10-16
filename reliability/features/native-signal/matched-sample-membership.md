# Matched sample membership

Capture: `artifacts/matched-sample-clock/20261016T120710656-f42b4a42dc9a404cac32cf1baa851b6a`.
Membership reports: `artifacts/matched-sample-membership/20261016T120849089`.
The membership commands use the analyzer retained in the capture's source snapshot.

```powershell
pwsh -NoProfile -File native/VerifyCooperativeComparison.ps1 -EvidenceDirectory artifacts/matched-sample-clock -SelectedBatches 500000 -ProbeInterval 8192 -PrepareReceipts -OwnerController -NominalSampleCredit -SelectedCpu -BoundSampleClock
```

## Collection and prediction

All four arms complete 500,000 selected batches with exit code zero.
The native, retained analyzer, and managed preflight checks pass.
Original inputs remain unchanged. The report contains no integrity or runner errors.
Both sampled clock captures are complete and consistent. Physical arms contain no sample-clock capture.

| Observation | Zero speedup | 50% speedup |
| --- | ---: | ---: |
| Selected-thread CPU | 2,126.4577 ms | 2,451.3898 ms |
| Source-inside selected nominal weight | 1,838 ms | 2,068 ms |
| Recorded sample credit | 0 ms | 1,034 ms |
| Source-inside unmapped nominal weight | 288 ms | 363 ms |

The sampled effective elapsed reduction is 33.2781–33.3152%.
The physical reduction is 46.0416–46.0428%.
The descriptive shortfall is 12.7264–12.7647 percentage points.
The runner exits 1 because prediction remains unqualified.
This single block does not establish an improvement over earlier runs.

The treatment freezes one peer request with a 499,837 ns budget and no confirmed child wait.
Its selected ledger is settled. The peer ledger remains 499,837 ns behind global delay at the endpoint.
Later cleanup does not retroactively settle frozen ownership.
Ownership, attribution, boundary assumptions, and calibration remain qualification limits.

## Source membership

| Sampled arm | Before | Inside | After | Ambiguous | Inside but unaccepted |
| --- | ---: | ---: | ---: | ---: | ---: |
| Zero speedup | 1 | 2,126 | 1 | 0 | 1 |
| 50% speedup | 2 | 2,431 | 1 | 1 | 0 |

All before-start samples belong to the accepted prefix and are unmapped.
The zero arm's inside-but-unaccepted sample is unmapped.
The treatment's ambiguous and after-window samples are outside the accepted prefix and are unmapped.
These samples carry zero recorded credit.

All 2,068 selected treatment samples are inside the source-time window and inside the frozen prefix.
All 1,034,000,000 ns of recorded treatment credit is therefore inside the observed window.
No retained selected observation is ambiguous or unaccepted.
This report shows no retained selected-credit boundary discrepancy that can explain the shortfall.
It does not bound unreceived observations or exact selected CPU.

## Next investigation

Treatment uses 324.9321 ms more selected-thread CPU than the zero arm.
Selected nominal sample weight rises by 230 ms. Unmapped inside weight rises by 75 ms.
These statistical weights are not exact CPU measurements or component-cost estimates.
The same work count does not prove identical execution cost under intervention.
Investigate recurring callback cost and inline instrumentation attribution without rewriting credit to force agreement.

The 42 Python comparison checks and PowerShell runner fixtures pass.
Both source reviews clear the capture configuration. Documentation passes `BuildDocs`.
See [the clock contract](../../../docsSrc/Reference/cooperative-comparison.md#bound-sample-clock-contract),
[membership policy](../../../docsSrc/Reference/bound-sample-membership.md), and [CPU explanation](../../../docsSrc/Explanations/selected-cpu.md).
