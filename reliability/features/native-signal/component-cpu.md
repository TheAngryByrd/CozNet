# Drain and service CPU capture

Final capture: `artifacts/component-cpu-final/20261016T093656047-50b39847`.

The workload uses 50,000 selected batches, probe interval 8192, nominal-event credit, prepared receipts, and synchronous owner/controller execution.
Owner draining, finite measurement, and direct receipt readiness are enabled.
The diagnostic observes every explicit owner drain and service call.

Zero speedup, 50-percent speedup, and expected invalidation all pass their declared outcomes.
All three processes exit zero. Replay exits zero. Original input hashes remain unchanged.
All four Linux managed preflight groups pass, including the component capture checks.
No component capture records a diagnostic failure.

## Measurement CPU

| Activity | Zero calls | Zero CPU, ms | Half calls | Half CPU, ms |
| --- | ---: | ---: | ---: | ---: |
| Drain | 100000 | 38.245600 | 100000 | 36.869900 |
| Skipped | 100000 | 18.621000 | 67262 | 11.995600 |
| Idle | 266 | 1.199000 | 98 | 0.805400 |
| Pending | 0 | 0 | 32397 | 12.941700 |
| Receipt | 0 | 0 | 262 | 11.163500 |
| Submit | 0 | 0 | 262 | 2.950800 |
| ReceiptAndSubmit | 0 | 0 | 0 | 0 |

The largest treatment drain bracket records 3.895100 ms CPU.
The largest treatment receipt bracket records 5.364200 ms CPU.
These brackets include observer work and can include first-use runtime work.
They do not identify JIT, native wait, or descheduling causes.

Both completed cases make 100,000 drain calls for 50,000 selected batches.
The source can change between calls. This count does not prove that one drain can be removed safely.
The treatment's pending and receipt CPU totals justify separate inspection of these paths.
Neither total predicts the benefit of removing checks or changing their cadence.

The expected invalidation case retains its CLR lifetime failure without a component diagnostic failure.
The helper checks separately exercise failed component calls and clock failure.

## Observer limits

Each observed call requires two CPU clock reads.
The CPU interval includes some observation overhead and excludes bucket updates after the ending reading.
The diagnostic can change sample attribution, selected pauses, receipt readiness, and capacity.
No CPU total changes credit, payment, or effective elapsed time.
This capture does not establish prediction accuracy or uninstrumented component costs.

## Rejected capture

`artifacts/component-cpu/20261016T093458741-3fa0820b` retains the initial attempt.
Its cases completed, but source edits during execution failed the unchanged-input gate.
That capture does not qualify the final implementation.
The final capture uses the rebuilt implementation and passes the unchanged-input gate.

## Controlled checks

`CooperativeComponentCpuChecks.TotalsAndSnapshots` checks exact totals, maxima, failed-call counts, snapshot isolation, and measurement separation.
`ServiceActivities` checks explicit service buckets with distinct CPU durations.
`FailuresAndOwnership` checks thread ownership, clock regression, nested regions, and missing starts.
Host execution through `--verify-bound-cooperative-managed` passes with injected clocks.
The same group passes in Linux preflight.

`ComponentCpuEvidenceChecks.py` passes four checks with corruption subcases.
They cover legacy records, activation, bucket shape, numeric limits, cadence reconciliation, and partial failed capture.
The sampled replay suite passes 32 checks. The selected replay suite passes 109 checks.
The PowerShell outcome checks reject missing or mismatched component activation.
They also preserve the original positional service-timing and credit-policy arguments.
The final replay rejects failed body calls in completed records while retaining them in incomplete records.
The stricter replay and mapping checks were added after capture. They do not change the producer's capture or payment behavior.

See [the capture procedure](../../../docsSrc/How_Tos/capture-component-cpu.md) and [field reference](../../../docsSrc/Reference/cooperative-component-cpu.md).
