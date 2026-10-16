# Prepared service ownership comparison

Capture: `artifacts/prepared-service-ownership/20261016T075609343` at source `5201fdd`.
The retained `plan.json` declares separate/owner/owner/separate order before execution.
`collect.ps1`, `outcomes.json`, `summarize.py`, and `comparison.json` retain collection and aggregation.
The summary command takes the capture directory and repository root as its two arguments.

## Protocol and integrity

Each group runs zero speedup, 50 percent, and expected invalidation after selected payment.
Normal cases perform 50,000 qualified-sample batches at probe interval 8192.
Receipt preparation, service timing, pause timing, owner draining, direct readiness, and finite measurement are enabled.
Dispatch timing and scheduler capture are disabled in both modes.
All payment, handler, receipt, and watchdog bounds remain unchanged.
No arm was retried or excluded.

All twelve expected outcomes pass. All four runners return zero with unchanged inputs.
Source hashes, binary hashes by filename, and SDK image identities match across groups.
The retained summaries report successful native, managed, outcome, and analyzer checks.
Independent aggregation replays every record and finds no integrity or setting mismatch.
All normal measurements remain unqualified. Expected invalidation remains an invalid measurement.

| Group | Capture |
| --- | --- |
| Separate 1 | `01-separate/20261016T075610394-e62100a2` |
| Owner 1 | `02-owner/20261016T075655563-ee28db06` |
| Owner 2 | `03-owner/20261016T075756492-cad4a655` |
| Separate 2 | `04-separate/20261016T075848427-08d9206c` |

## Measured treatment service

These values use `serviceTiming.Measurement`, not lifetime counters.
No service call failed. Receipt-and-submit buckets contain no calls.

| Group | Pending ms | Receipt calls | Receipt ms | Mean receipt µs | Submit calls | Submit ms | Mean submit µs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Separate 1 | 12.380206 | 254 | 46.983308 | 184.974 | 255 | 28.781810 | 112.870 |
| Owner 1 | 8.396414 | 192 | 9.583301 | 49.913 | 192 | 2.686348 | 13.991 |
| Owner 2 | 8.807750 | 196 | 13.319717 | 67.958 | 197 | 2.834106 | 14.386 |
| Separate 2 | 15.136056 | 275 | 56.929584 | 207.017 | 276 | 31.662915 | 114.721 |

## Frozen elapsed bounds

Effective elapsed time subtracts the frozen global-delay delta from elapsed time.
These descriptive values cover 50,000 selected progress units.

| Group | Zero effective ms | Treatment effective ms |
| --- | ---: | ---: |
| Separate 1 | 232.006059–232.047629 | 282.345130–283.125338 |
| Owner 1 | 249.607384–249.644434 | 177.071182–177.237375 |
| Owner 2 | 252.016146–252.059825 | 196.844271–197.566622 |
| Separate 2 | 220.570596–220.602970 | 300.921691–301.593057 |

## Decision and limits

Both owner groups record shorter receipt and submission paths, in total and per call.
Their treatment effective elapsed bounds are also lower than both separate-controller groups.
This supports a prepared owner/controller comparison against the physical optimization.
It does not establish general performance or prediction accuracy.
Zero-speedup times vary between modes. Call counts and source sampling also differ.
The fixed order reduces position imbalance but does not establish statistical confidence.
Elapsed brackets include observer work, scheduling, native calls, and managed accounting.
They do not isolate dispatch CPU cost. Pause and service brackets can overlap.

See [the service reference](../../../docsSrc/Reference/cooperative-service-timing.md) and [the cost explanation](../../../docsSrc/Explanations/cooperative-pause-cost.md).
