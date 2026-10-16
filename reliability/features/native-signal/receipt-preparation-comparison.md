# Managed receipt preparation comparison

Capture root: `artifacts/receipt-preparation-comparison/20261016T071048325`.
The groups run in unprepared/prepared/prepared/unprepared order.
Each group uses interval 8192, 50,000 selected batches, owner draining, direct receipt readiness, finite measurement, pause timing, and dispatch timing.
Prepared groups add `-PrepareReceipts`.

All twelve expected outcomes pass, including four intended lifetime invalidations.
Every summary reports unchanged inputs. Source hashes, image identity, and binary hashes match across groups.
Binary comparison uses filenames because each native output directory has a different capture path.
No failed case was discarded or repeated.

| Group | Capture |
| --- | --- |
| Unprepared 1 | `unprepared-1/20261016T071050182-359ec938` |
| Prepared 1 | `prepared-1/20261016T071144228-308e6363` |
| Prepared 2 | `prepared-2/20261016T071233608-36988ab2` |
| Unprepared 2 | `unprepared-2/20261016T071335795-750c1bf7` |

## First peer request in treatment

Each duration uses managed timestamps and the record's `timestampFrequency`.
The read action matches operation `PeerRead`, command 1, generation 1 in the retained dispatch prefix.

| Group | Request bracket, ms | Read action, ms | Preparation observation, ms | Preparation acceptance, ms |
| --- | ---: | ---: | ---: | ---: |
| Unprepared 1 | 37.167389 | 16.521962 | — | — |
| Prepared 1 | 13.547552 | 2.524328 | 3.249934 | 8.354018 |
| Prepared 2 | 12.415393 | 2.372509 | 5.307581 | 24.169317 |
| Unprepared 2 | 23.147211 | 11.384528 | — | — |

Both prepared groups have shorter first read actions and request brackets than both unprepared groups.
Preparation moves a controlled managed exercise before measurement. Its brackets do not measure total setup cost.
The preparation dispatch can also initialize shared controller paths.
This comparison does not isolate receipt parsing from ledger acceptance or prove that all first-use cost has been removed.

## Whole-measurement limits

| Group | Zero effective cost, ns/progress | Treatment effective cost, ns/progress |
| --- | ---: | ---: |
| Unprepared 1 | 4671.53594–4672.29968 | 6249.13418–6254.20192 |
| Prepared 1 | 4767.88848–4768.73822 | 4180.13674–4194.98764 |
| Prepared 2 | 4420.83724–4421.66888 | 4111.22304–4115.36338 |
| Unprepared 2 | 5874.92982–5875.83456 | 5580.38558–5596.15272 |

All eight normal measurements remain unqualified.
Every initial measurement snapshot has zero global delay. The synthetic preparation payment does not enter the live ledger.
Treatment residuals, source tails, sample membership, and instrumentation attribution remain unqualified.
Prepared 1 and Unprepared 2 also retain native ownership at the frozen boundary.
The table contains descriptive clock brackets, not confidence intervals or prediction accuracy bounds.
Zero-speedup costs vary substantially. The comparison does not establish an overall speedup.

## Validation scope

Managed checks cover mode parsing, disabled execution, controller execution, exact synthetic settlement, and dispatch failure behavior.
Python checks cover mode metadata, settlement fields, timestamp order, and premeasurement completion.
PowerShell checks bind enabled and disabled runner requests to Boolean result metadata.
State isolation follows from local fixture construction and the absence of live state in the preparation API.
Controller placement and preparation before sampling follow from source ordering, not from the timestamp replay alone.

The matched captures precede final failure-evidence retention and test cleanup.
Those changes retain rejected preparation results and remove a redundant receipt-count check and immutable-state assertion.
The final analyzer calls the preparation verifier directly from each record verifier.
No prepared receipt or accounting algorithm changed after the matched captures.

Final prepared verification: `artifacts/receipt-preparation-final/20261016T071717224-bc7594b9`.
All three expected outcomes pass with unchanged inputs and ten prepared children in each record.
The normal measurements remain unqualified, and intended invalidation remains invalid.
This verifies the final code path. It is not another matched performance group.

The next accuracy experiment needs a prepared four-arm physical comparison.
The default remains unprepared until broader capacity and accuracy evidence supports a change.
See [the procedure](../../../docsSrc/How_Tos/compare-receipt-preparation.md)
and [the explanation](../../../docsSrc/Explanations/cooperative-pause-cost.md#managed-preparation-comparison).
