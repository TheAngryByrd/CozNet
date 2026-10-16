# Cooperative component CPU

`COZNET_COMPONENT_CPU=1` enables CPU observations around owner drains and peer service calls in the bound sampled workload.
Absence or `0` disables capture. Other values fail parsing.
Capture requires finite measurement and an `IThreadCpuClock` platform hook.
The Linux workload supplies `LinuxThreadCpuClock`.

## Output

`componentCpuEnabled` records activation. `componentCpuEvidenceVersion` is `1`.
`componentCpu` is null when capture is disabled.
An enabled capture has `All`, `Measurement`, `Active`, and `Failure` fields.
`Measurement` contains calls entered during the active measurement window.
`All` also contains calls outside that window, including final draining.

Each array has seven buckets in this order:

| Activity | Scope |
| --- | --- |
| Drain | Explicit owner source drain |
| Skipped | Service skipped by idle cadence |
| Idle | Service without a pending request or submission |
| Pending | Pending request without an accepted receipt |
| Receipt | Receipt acceptance without another submission |
| Submit | New request submission |
| ReceiptAndSubmit | Receipt acceptance and another submission |

Each bucket records `Activity`, `Calls`, `Failures`, `CpuNanoseconds`, and `MaximumNanoseconds`.
Activity values serialize as integers from 0 through 6.
Failed calls use the activity reached before failure.
`Failure` records a diagnostic error. A failed diagnostic rejects further observations.
`Active` identifies an unfinished region.

## Limits

Capture performs two CPU clock reads per observed call.
The CPU interval includes some observer work. It excludes bucket updates after the ending reading.
Draining excludes subsequent sample publication and selected payment.
Service includes synchronous controller work on the selected owner, but excludes CPU consumed on a separate controller thread.
Clock reads can change sample timing, receipt timing, and debt failures.
Component totals do not estimate the unobserved workload or correct the causal estimate.
Concurrent or nested regions are rejected. All observations require the owning managed thread.
A diagnostic error in a completion handler can replace the original workload exception.
If CPU completion fails while service timing is enabled, that call has no service-timing record.
Service-timing replay then rejects the incomplete record. This combination does not preserve complete evidence for both diagnostics after clock failure.

See [the capture procedure](../How_Tos/capture-component-cpu.md) and [CPU interpretation](../Explanations/selected-cpu.md).
