# Selected-thread CPU comparison

Capture: `artifacts/selected-cpu-comparison/20261016T091238858-c05007a3f18f4250a8a8d0bce6017542`.

The comparison uses 50,000 selected batches, interval 8192, nominal-event credit, prepared receipts, owner/controller execution, and selected CPU capture.
All four processes exit zero. All four CPU captures complete and pass replay.
Collection completes with zero invalid arms, no integrity errors, and no runner errors.
Original inputs remain unchanged. The runner exits 1 because prediction remains unqualified.

## CPU observations

| Arm | Selected CPU, ms | Elapsed lower, ms | Elapsed upper, ms |
| --- | ---: | ---: | ---: |
| Physical baseline | 165.477600 | 165.491777 | 165.565430 |
| Physical optimized | 80.030000 | 80.097053 | 80.153059 |
| Sampled zero | 221.896700 | 221.902618 | 221.916932 |
| Sampled half | 276.967300 | 276.976292 | 276.991678 |

Elapsed bounds bracket the two CPU reads. They are narrower than the full measurement window.
They are not confidence intervals. Clock resolution and observation cost remain limitations.
The sampled zero bracket exceeds CPU advance by 5,918–20,232 ns.
The sampled half bracket exceeds CPU advance by 8,992–24,378 ns.
Thus, this capture does not support substantial descheduling inside those observed selected-work brackets.

The sampled half arm consumes 55.070600 ms more selected-thread CPU than the sampled zero arm.
The zero arm consumes 56.419100 ms more than the physical baseline.
These are descriptive differences from one block, not isolated observer costs.
The sampled CPU interval includes probes, drains, service, selected payment, runtime work, and arithmetic.
It cannot identify which component accounts for the difference.

## Causal comparison

The sampled effective elapsed reduction is **21.4716–21.5868%**.
The physical elapsed reduction is **51.4617–51.4730%**.
The descriptive shortfall is **29.8748–30.0014 percentage points**.
CPU observations do not enter this calculation.

The treatment freezes 265 accepted samples from 268 source records.
It credits 206 mapped events, for 103,000,000 ns.
Frozen global delay is 103,008,761 ns.
Raw elapsed is 277,705,727–277,934,227 ns.
Effective elapsed is 174,696,966–174,925,466 ns after subtracting frozen global delay.
The treatment records 34 selected pauses, 244 admitted checks, and 8,669 ns of final selected payment.
The zero arm records no pauses and 211 admitted checks.
Both source records report zero loss and throttling.

Source-tail membership, attribution, and a treatment ledger residual remain unqualified.
Local optimization calibration and the single-block limit also remain unresolved.
The next investigation should isolate CPU consumed by recurring probe, drain, and service paths.
The current data do not establish that reducing any one path will close the prediction gap.

## Verification

Managed capture checks pass on Windows with injected clocks and on Linux during capture preflight.
They cover parsing, preparation exclusion, ownership, invalid transitions, regressions, and zero CPU advance.
The comparison suite passes 38 checks. Selected replay passes 109 checks. The controlled PowerShell runner checks pass.
The versioned collection check verifies all four CPU values, preserves other arms after missing metadata, and rejects a missing replay snapshot.
Legacy prepared-owner comparison replay remains unchanged except for the earlier default credit-policy metadata.

See [the reference](../../../docsSrc/Reference/selected-cpu.md), [interpretation](../../../docsSrc/Explanations/selected-cpu.md), and [capture procedure](../../../docsSrc/How_Tos/capture-selected-cpu.md).
