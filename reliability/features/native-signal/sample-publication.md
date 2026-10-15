# Sample publication service cadence

## Contract

`BoundSamplePublication` freezes the input count and processes the requested suffix in order.
Default mode services the peer after each successfully processed sample.
Batch mode services the peer after a nonempty batch.
Every sample still runs the workload's selected and peer 2 ms debt gates.
A processing failure prevents service and later samples. A service failure prevents further processing without retry.
The 50 µs child request, 5 ms handler, and 100 ms receipt limits remain unchanged.

## Controlled evidence

The .NET 10 workload runs these checks through `--verify-bound-cooperative-managed` on Windows and Linux.

| Requirement | Check |
| --- | --- |
| Preserve order, offset, and cumulative count for both modes | `ServiceFollowsDeclaredSampleBoundary` |
| Stop on intermediate sample failure with original exception | `SampleFailureStopsBeforeServiceAndLaterSamples` |
| Stop on service failure with original exception and processed prefix | `ServiceFailurePreservesOriginalExceptionAndPrefix` |
| Do not chase later samples | `FrozenPrefixExcludesNewSamples` |
| Reject invalid modes and offsets without work | `EmptyAndInvalidInputsDoNotService` |
| Check publication mode and count in peer replay | `test_publication_mode_and_service_count_are_checked` |
| Check publication evidence in selected replay | `test_selected_replay_checks_publication_mode_and_count` |

The Python suites pass 21 peer and 52 selected checks.
The outcome-reader fixtures reject missing, malformed, and mismatched mode values for each requested Boolean.
The live runner also passes all three managed groups and thirteen native checks before its first capture.
The cumulative-count assertion was added after live capture. Production code remains unchanged.

## Live evidence

Unrestricted batch capture: `artifacts/batch-service-baseline/20261015T215339625-fbfc8f4b`.
The runner uses owner draining, 50,000 requested batches, qualified-sample cadence, pause timing, and child CPU capture.
Zero speedup completes 50,000 batches. Expected invalidation passes after selected payment.
Treatment fails after 30,203 batches with a failed peer journal.
That journal retains a 9,374,041 ns child. It exceeds the 5 ms handler limit and does not authorize payment.
Treatment processes 368 samples with 327 publication-triggered service attempts.
Replay verifies all three records and preserves the treatment failure.

Single-CPU capture: `artifacts/batch-service-single/20261015T215459946`.
The runner declares per-sample, batch, batch, per-sample order before execution.
It uses the baseline binaries, the same fixed work and diagnostics, and guest CPU 0.
It retains command arrays, source and binary hashes, affinity evidence, raw outcomes, and replay reports.
The CPU restriction also changes processor availability for runtime policy.

| Trial | Treatment batches | Publication service calls | Processed samples | Outcome |
| --- | ---: | ---: | ---: | --- |
| Per-sample 1 | 4,791 | 283 | 284 | Peer debt 2,499,949 ns |
| Batch 1 | 4,672 | 236 | 266 | Peer debt 2,499,939 ns |
| Batch 2 | 2,971 | 146 | 180 | Peer debt 2,499,929 ns |
| Per-sample 2 | 50,000 | 2,685 | 2,685 | Passed |

All zero-speedup and expected-invalidation cases pass. All twelve records replay.
Input hashes remain unchanged during capture. The comparison retains all three treatment failures.
The declared repetitions are comparison trials. No failed trial was replaced or retried to obtain a pass.
Local artifacts are not distributed with this document.

## Accepted limits

Portable checks exercise the publication helper. They do not execute the workload's local `Process` function with injected ledger failures.
Source review verifies both call sites and the retained per-sample `ObserveDebt` call.
Putting an extra `Service` call into `Process` would not fail the helper checks or publication count checks.
This is an explicit wiring-coverage gap, not runtime proof of all workload interleavings.

Batch mode can reach a debt gate before accepting an already-finished peer request.
It can also reach the existing receipt watchdog before deferred service.
These are deliberate scheduling differences. Neither permits debt forgiveness or replacement of an unaccepted receipt with payment.

The helper freezes input length, not element values. Its current callers supply snapshots with stable existing elements.
Concurrent replacement or removal is outside its contract.

The count records only attempted publication-triggered service, including a failed call.
Probe, outer-loop, and final settlement calls remain outside that count.
The cadence probe can call service again immediately after publication. That existing call remains active in both modes.
Removing it would change another scheduling boundary and belongs in a separate comparison.

Replay cannot distinguish modes when every publication contains one sample.
It checks count bounds and reported mode. The full runner binds mode to its requested environment setting.
The count does not reconstruct exact batch history. A smaller corrupted count can satisfy the batch-mode bounds.
Legacy records without both fields remain readable.
The comparison does not isolate controller cost or establish stable capacity or prediction accuracy.
Failed treatments cannot support an equal-work overhead estimate.

See [publication reference](../../../docsSrc/Reference/bound-sampled-payment.md#sample-publication-cadence),
[capacity explanation](../../../docsSrc/Explanations/owner-drain-capacity.md#batch-service-does-not-establish-stable-capacity),
and [the comparison procedure](../../../docsSrc/How_Tos/run-bound-selected-cadence.md#compare-peer-service-after-sample-batches).
