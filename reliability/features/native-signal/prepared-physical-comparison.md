# Prepared four-arm physical comparison

Capture: `artifacts/prepared-physical-comparison/20261016T072633327-4c915fadd46842778823fd800d84873c`.

```powershell
pwsh -NoProfile -File native/VerifyCooperativeComparison.ps1 `
  -EvidenceDirectory artifacts/prepared-physical-comparison -ProbeInterval 8192 `
  -PrepareReceipts -SelectedBatches 50000
```

All four arms complete with process exit zero. No arm is invalid.
The analyzer reports no runner or integrity errors. Original inputs remain unchanged.
The runner exits one because prediction remains unqualified.
The plan and analysis report `prepareReceipts=true` and `receiptPreparationEvidenceVersion=1`.
Both sampled records contain enabled preparation with ten settled children and 500,000 ns of synthetic payment.
Physical arms run without profiling or receipt preparation.

## Descriptive result

| Arm | Lower effective cost, ns/progress | Upper effective cost, ns/progress |
| --- | ---: | ---: |
| Physical baseline | 3530.28938 | 3530.55094 |
| Physical optimized | 1708.93878 | 1709.20552 |
| Sampled zero | 4624.34442 | 4625.04350 |
| Sampled half | 4169.84262 | 4183.90162 |

Effective elapsed time subtracts the frozen global-delay delta from recorded elapsed time.
The sampled effective elapsed reduction is 9.5244% to 9.8421%.
The physical elapsed reduction is 51.5845% to 51.5957%.
The sampled result remains approximately 41.74–42.07 percentage points below the physical result.
These ranges describe recorded clock brackets, not confidence intervals or qualified accuracy bounds.
The earlier unprepared block is a separate capture and cannot isolate preparation's performance effect.

## Remaining evidence limits

Treatment freezes peer command 203 with a 499,886 ns budget and no accepted child.
Its frozen global delay is 86,006,387 ns.
Its effective elapsed interval is 208,492,131–209,195,081 ns for 50,000 selected progress units.
Native ownership, ledger residuals, source tails, sample membership, and instrumentation attribution remain unqualified.
Local physical optimization calibration and the single-block comparison also remain unqualified.

Zero speedup executes 231 service checks and skips 100,000.
Treatment executes 39,926 service checks and skips 60,331.
These counters include final draining. They do not measure time or establish which checks caused the mismatch.
The difference supports separate investigation of recurring service work while peer requests are pending.
Preparation addresses first-use managed paths. It does not remove recurring receipt, accounting, probe, or scheduling costs.

Treatment records 31 complete selected pauses. Their wall-time brackets total 12.799052 ms.
Start through callback completion totals 12.036872 ms. Callback completion through resume totals 0.762180 ms.
Recorded selected payment is 0.006387 ms. These intervals overlap and must not be added as independent costs.
The pause brackets do not measure all service work or isolate CPU cost.

No failed timing arm was discarded or repeated for a passing result.
The result does not justify changing the default preparation mode or relaxing accounting bounds.

## Contract checks

All 29 analyzer checks pass. Runner checks cover prepared and unprepared activation, both probe intervals, and original positional arguments.
Versioned plans require explicit Boolean preparation settings and matching metadata in both sampled records.
Full-replay collection checks invalidate a missing or mismatched sampled arm, preserve the other three costs, and suppress aggregate comparison.
Legacy plans permit only unprepared records. Physical arms require no preparation metadata.

See [the preparation contract](../../../docsSrc/Reference/cooperative-comparison.md#receipt-preparation-contract)
and [the explanation](../../../docsSrc/Explanations/cooperative-physical-comparison.md#comparison-with-prepared-managed-receipts).
