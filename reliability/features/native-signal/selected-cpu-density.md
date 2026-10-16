# Selected CPU and probe density

These captures compare probe intervals with unchanged source and managed binaries.

- Interval 1024: `artifacts/selected-cpu-dense/20261016T092448130-6d425125614d4dabbf19b163c4ee1c59`.
- Interval 8192: `artifacts/selected-cpu-sparse-pair/20261016T092607076-6d4c0d3e6996400aa2f0c4039d2ab9ad`.

Both use 50,000 selected batches, nominal-event credit, prepared receipts, owner/controller execution, and selected CPU capture.
The 1024 capture ran first. Each capture uses the declared fixed four-arm order.
Every original input hash matches between manifests. Each capture reports no integrity or runner errors.
All eight processes exit zero. Both analyses report zero invalid arms.
Both runners exit 1 because prediction remains unqualified.

## Selected CPU

| Arm | Interval 1024, ms | Interval 8192, ms |
| --- | ---: | ---: |
| Physical baseline | 159.184900 | 164.513100 |
| Physical optimized | 79.315100 | 90.195100 |
| Sampled zero | 380.758700 | 236.065400 |
| Sampled half | 426.183500 | 268.035000 |

Sampled zero uses 144.693300 ms less CPU in the sparse capture.
Sampled half uses 158.148500 ms less CPU in the sparse capture.
The treatment-minus-zero difference is 45.424800 ms at 1024 and 31.969600 ms at 8192.
These differences describe two captures. They do not isolate probe instructions, draining, service, or payment CPU.
The physical CPU values also differ between captures. Run order and scheduling remain uncontrolled.

## Service and selected payment

| Observation | 1024 zero | 1024 half | 8192 zero | 8192 half |
| --- | ---: | ---: | ---: | ---: |
| Service checks | 500374 | 500409 | 100233 | 100264 |
| Service executions | 374 | 117529 | 233 | 48935 |
| Skipped service checks | 500000 | 382880 | 100000 | 51329 |
| Admitted selected checks | 362 | 391 | 221 | 240 |
| Selected pauses | 0 | 189 | 0 | 32 |
| Credited delay, ns | 0 | 99000000 | 0 | 103500000 |
| Selected payment, ns | 0 | 202123 | 0 | 31148 |

Service counters include final draining. They are not CPU measurements.
Changing probe density also changes sample observation and payment scheduling.
The comparison therefore does not measure the isolated cost of one probe.

## Prediction

| Reduction | Interval 1024 | Interval 8192 |
| --- | ---: | ---: |
| Sampled effective elapsed | 13.9307–13.9802% | 29.6570–30.2168% |
| Physical elapsed | 49.9838–49.9962% | 44.7637–44.7766% |
| Physical minus sampled | 36.0036–36.0655 percentage points | 14.5469–15.1196 percentage points |

These ranges describe endpoint uncertainty. They are not statistical confidence intervals.
Effective elapsed time subtracts the frozen increase in global delay from raw elapsed time.
Both captures retain unqualified source-tail membership, attribution, treatment ledger residuals, and local optimization calibration.
Neither capture establishes prediction accuracy or general platform support.

## Consequence

Lower probe density remains useful for further experiments, but it does not resolve the prediction gap.
Unchanged managed accounting state cannot prove that another source drain is redundant.
Native samples can arrive between the selected probe and the outer-loop drain.
Pending service must also retain its receipt timeout and debt checks.

The next component experiment should distinguish pending-service CPU from source-drain CPU at a fixed probe interval.
It must retain the current debt, lease, child, handler, and receipt bounds.

See [CPU interpretation](../../../docsSrc/Explanations/selected-cpu.md) and [capture procedure](../../../docsSrc/How_Tos/capture-selected-cpu.md).
