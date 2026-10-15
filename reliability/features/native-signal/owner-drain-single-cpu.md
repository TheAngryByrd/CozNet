# Owner-driven collection on one guest CPU

## Contract and method

Source revision: `82c39bca6b1536e489158179d1d918e5a89d4451`.
Capture: `artifacts/owner-drain-single-cpu/20261015T212121304`.
Binary baseline: `artifacts/owner-drain-final/20261015T211653127-b8a9d532`.
The baseline retains all 213 source hashes and five binary hashes.
Those hashes match before and after every trial.
The retained `runner.ps1` reuses those binaries and the baseline analyzer.
Local artifacts are not distributed with this document.

The runner declares background, owner, owner, background before execution.
Each trial runs zero speedup, 50 percent, and expected invalidation serially.
Each case requests 50,000 selected batches with qualified-sample cadence.
Child CPU observations and pause timing are enabled in both modes.
Each process has a 30-second watchdog.

Docker restricts each workload to guest CPU 0.
The affinity probe reports `Cpus_allowed_list: 0`.
Each retained command array includes `--cpuset-cpus 0` and its explicit `COZNET_OWNER_DRAIN` value.
The image is `sha256:3db72bd242b5f26a45d74c2d49a7654740e75c732654658177e52aa498645c14`.
The restriction also changes processor availability for runtime policy.
This does not isolate OS scheduling from runtime policy or host effects.

The 2 ms debt, 50 microsecond requested child, 5 ms handler, and 100 ms receipt limits remain unchanged.
Every result matches its requested mode, cadence, work count, and diagnostic flags.
No case was rerun outside the declared order.

## Outcomes

| Trial | Zero-speedup elapsed | Treatment batches | Treatment failure | Maximum retained handler |
| --- | ---: | ---: | --- | ---: |
| 01 background | 876.122174 ms | 8,218 | Peer debt 2,499,921 ns | 1,097,388 ns |
| 02 owner | 888.316933 ms | 77 | Request-to-receipt interval rejected | 86,226 ns |
| 03 owner | 829.896041 ms | 561 | Peer debt 2,499,927 ns | 62,104 ns |
| 04 background | 868.135212 ms | 9,559 | Peer debt 2,000,056 ns | 1,445,096 ns |

All four zero-speedup cases complete 50,000 batches without payment.
All four expected-invalidation cases preserve the intended rejection.
All four treatments fail. Replay verifies all twelve records and preserves all failures.
No retained handler exceeds 5 ms. Cleanup reports no error in the treatment records.
The maximum includes accepted, failure-capture, and cleanup child records on both slots.
`comparison-metrics.json` retains the derived table values.
No treatment has a completed elapsed endpoint for a fixed-work comparison.

## Owner trial 03: new credit during pending service

Peer command/generation 5 requests 999,927 ns.
Its first child starts 21,999,711 ns after native arming.
The last debt observation occurs 12,749,702 ns after the managed request start.
That observation precedes the first native child by 9,264,447 ns under the shared monotonic-clock contract.

Credited sample indices 71, 73, and 74 each add 500,000 ns.
Their source timestamps follow native arming by 4,753,112, 10,671,935, and 12,703,441 ns.
The final arithmetic is `999927 + 3 * 500000 = 2499927`.
No accepted payment intervenes after the request starts.
These credits are not samples buffered before this request was armed.
They differ from the earlier [background backlog failure](cooperative-single-cpu.md#buffered-credit-at-the-debt-boundary).

The later failure snapshot retains twenty children and 999,992 ns of measured wait.
Those waits occur after the debt failure and do not settle the rejected experiment.
The evidence does not show whether the peer was runnable, blocked, or delayed by the host before its first child.

## Owner trial 02: a missing rejected receipt endpoint

The first peer request has a 500,000 ns budget.
Native arming follows the managed request start by 31,973,991 ns.
The first child starts 15,467,021 ns after native arming.
All ten children finish 537,761 ns after the first child starts.
Their aggregate measured wait is 500,078 ns.

The retained accounting observation captures the native receipt 16,266,879 ns after the last child finishes.
Its native clock upper reading is 65,424,388 ns after the managed request start under the shared clock contract.
The outer failure capture starts 112,028,591 ns after that managed request start.

The exception is:

```text
The cooperative request-to-receipt interval exceeds 100 ms or has invalid clock evidence.
```

`Service` calls `CooperativeReceiptInterval.Parse` after `OnController(ReadAndAccept)` returns.
The failed parse does not retain its `observed` argument.
The native receipt reading therefore does not establish the rejected managed endpoint.
The later failure capture includes exception handling and cannot substitute for that endpoint.
This evidence cannot divide the gap among controller work, return scheduling, and error handling.
No payment enters the workload's accepted totals after this rejection.

## Next verification

Retain the exact rejected managed receipt endpoints and request identity before changing receipt handling.
Keep the existing receipt and debt gates.
Owner draining alone does not establish capacity on one CPU.
A separate batch-order experiment must preserve intermediate debt observations and raw receipt identities.

See [the explanation](../../../docsSrc/Explanations/owner-drain-capacity.md),
[the collector contract](../../../docsSrc/Reference/pausable-sample-collector.md),
and [the source comparison](../../../docsSrc/Research/native-owner-scheduling.md).
