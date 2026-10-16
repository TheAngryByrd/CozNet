# Cooperative pause and controller costs

This page explains what the retained phase capture establishes about selected payment cost.
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
