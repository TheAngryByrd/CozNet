# Cooperative service timing

`COZNET_SERVICE_TIMING=1` enables elapsed-time observations around each peer `Service()` call in the bound sampled workload.
The runner option is `-ServiceTiming`. Finite measurement is required.
An absent value or `0` disables capture. Other values fail parsing.
The observer uses portable managed timestamps. The live sampling backend remains Linux-specific.

## Record fields

`serviceTimingEnabled` records activation. `serviceTiming` is null when disabled.
When enabled, `serviceTiming` contains two arrays with six ordered buckets:

- `All` contains every service call, including final draining.
- `Measurement` contains calls entered while the finite measurement window was active.

| Activity | Value | Attempted path |
| --- | --- | --- |
| `Skipped` | 0 | The cadence check skipped service. |
| `Idle` | 1 | Service ran without receipt acceptance or submission. |
| `Pending` | 2 | Service checked an outstanding request without accepting its receipt. |
| `Receipt` | 3 | Service processed a ready receipt without a new submission. |
| `Submit` | 4 | Service attempted a new submission without processing a receipt. |
| `ReceiptAndSubmit` | 5 | Service processed a receipt and attempted another submission. |

Each call belongs to one bucket. The bucket records the path reached before completion or failure.
Each bucket contains `Activity`, `Calls`, `Failures`, `ElapsedTicks`, and `MaximumTicks`.
`timestampFrequency` converts managed ticks to seconds.
`Failures` counts calls that exited before successful service completion.

## Timing limits

The bracket includes the cadence check, receipt operations, possible submission, and debt observation on the attempted path.
Observer bookkeeping follows the ending timestamp. It adds workload cost outside the bracket.
Elapsed time includes scheduling delays and controller waits. It is not CPU time.
Pause timing can overlap service timing. Their totals are not independent costs.
The buckets do not isolate the native readiness lock or prove that polling causes prediction error.

Replay checks bucket order, totals, measurement subsets, cadence counts, and closed measurement-window bounds.
Calls outside measurement must account for the remaining elapsed time and lifetime maximum.
Completed workloads cannot contain failed service calls.
Legacy records without both timing fields remain valid.
Runner acceptance requires activation metadata that matches the requested option.

See [the capture procedure](../How_Tos/measure-cooperative-service.md) and [observer overhead](../Explanations/bound-observer-overhead.md).
