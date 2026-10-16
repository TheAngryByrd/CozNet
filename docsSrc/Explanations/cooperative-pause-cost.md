# Cooperative pause and controller costs

This page explains what the retained captures establish about selected payment and controller cost.
The sampled experiment remains unqualified.

The treatment completed 50,000 selected batches with 146 complete pause records.
Those pauses totaled 39.796340 ms.
The interval from sample publication to callback return accounted for 26.240063 ms.
Selected payment intervals totaled 24.200458 ms, but accepted native selected waits totaled only 0.079340 ms.
These intervals overlap. Adding them would count time more than once.

The difference does not establish removable overhead.
Payment intervals include submission, polling, receipt acceptance, and release.
The phase observer also adds clock reads, synchronization, and allocation.
This capture does not isolate those costs.

## Controller evidence

The treatment recorded 1,344 controller dispatch calls and 76,350 receipt-readiness calls.
Only 128 recent dispatch records survive, together with independent maxima.
Of that recent history, 126 complete calls fall wholly inside the conservative measurement window.
Queue and return totals exceed action totals for each operation in this retained subset.
The subset does not establish totals or average costs for all dispatches.
Global maxima can describe setup or cleanup.

The evidence supports investigation of controller round trips.
The [owner/controller proposal](../Research/cooperative-owner-controller.md) describes one candidate and its required checks.
It does not establish that spinning, combined operations, or owner-thread execution will improve prediction.
Receipt acceptance and release have separate accounting and ownership effects.
An optimization must preserve the receipt endpoint and retain accepted ledger state if release fails.

## Matched owner/controller comparison

A later comparison uses the same source, binaries, image, work, and observer settings for both controller modes.
It runs four groups in separate/owner/owner/separate order.
All twelve expected checks pass, including invalidation and cleanup.
The normal cases each complete 50,000 selected batches.

| Group | Total selected pause, ms | Median selected receipt, ms | Maximum peer receipt, ms |
| --- | ---: | ---: | ---: |
| Separate 1 | 33.258080 | 0.113573 | 20.504020 |
| Owner 1 | 21.053453 | 0.012260 | 14.683500 |
| Owner 2 | 23.573216 | 0.012534 | 14.949722 |
| Separate 2 | 40.158204 | 0.123344 | 16.860826 |

Both owner groups have shorter observed selected receipt and callback intervals than both separate groups.
This supports further controlled use of the opt-in mode.
It does not isolate dispatch savings. Payment counts, selected credit, and accepted wait differ across groups.
The mode also changes where controller CPU executes and can change sampled attribution.

The zero-speedup elapsed intervals range from approximately 401–407 ms to 558–567 ms across groups.
Descriptive effective reductions vary from negative to positive, even between the two separate-controller groups.
Thus, lower selected pause cost does not establish an overall speedup or a more accurate causal prediction.
All normal measurements remain unqualified.

The [retained comparison](../../reliability/features/native-signal/owner-controller-comparison.md) gives the exact settings, arithmetic, and failure limits.
Owner/controller mode remains opt-in. Accounting bounds and the default remain unchanged.
Further accuracy work needs measurement-boundary evidence and a calibrated physical optimization.

## First-request evidence at interval 8192

A separate capture uses probe interval 8192 with pause timing and dispatch timing enabled.
Both normal cases complete 50,000 selected batches. The intended lifetime invalidation also passes its protocol check.
The inputs remain unchanged, but both normal measurements remain unqualified.

The treatment records 239 peer requests. The first request's managed bracket lasts 21.742797 ms.
The median request bracket lasts 0.718808 ms.
These brackets overlap selected work. Their sum does not measure selected-thread overhead.

The dispatch observer records 810 calls with no failed or incomplete calls.
Its recent history retains sequences 683 through 810. No first-request dispatch survives.
All four maxima describe session creation, before measurement starts.
That call takes 43.405592 ms, including a 36.158529 ms action bracket.
Thus, the retained maxima cannot locate the first request's delay within queue, action, or return.
The recent tail cannot supply the missing first-request evidence.

Clock domains impose another limit.
`FirstManagedReceiptUpper` comes from the native clock, despite its name.
Request and dispatch brackets use `Stopwatch`.
Subtracting timestamps across these domains does not establish a component duration.
Equal timestamp frequencies do not establish a common clock origin.

New captures retain the first 32 dispatches as a versioned prefix.
This preserves first-request phases that occur within that prefix. It cannot restore missing records in older captures.
Further attribution needs a new capture or a controlled component diagnostic.
This capture does not identify receipt parsing, ledger acceptance, JIT, GC, or scheduling as the cause.

A follow-up capture retains the first request's submit, read, and release dispatches in the prefix.
All expected protocol outcomes pass, but prediction remains unqualified.
The first request lasts 22.820403 ms. Its read action bracket lasts 10.731190 ms.
That action includes native receipt reading and managed accounting acceptance.
The bracket does not separate these components or exclude descheduling.
This supports a focused receipt-path diagnostic, rather than a claim that controller queueing explains the first-request cost.

The [retained evidence](../../reliability/features/native-signal/sparse-first-receipt-timing.md) gives the settings and exact brackets.

## Measurement limits

Both completed cases remain unqualified for attribution, sample boundaries, and source tails.
The treatment also retains an unqualified ledger residual.
The intended invalidation case passed its protocol check but produced an invalid measurement.
Its incomplete pause has no complete duration. A zero sum of complete records does not mean zero elapsed time.

No unchanged timing retry contributed to this result.
The diagnostic does not replace the first matched physical comparison or establish accuracy bounds.

See [the retained phase evidence](../../reliability/features/native-signal/cooperative-cost-phases.md),
[pause fields](../Reference/bound-selected-cadence.md#optional-pause-timing),
[dispatch fields](../Reference/cooperative-managed.md#controller-dispatch-timing),
and [the physical comparison](cooperative-physical-comparison.md).
