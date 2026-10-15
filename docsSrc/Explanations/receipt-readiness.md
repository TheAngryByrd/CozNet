# Why direct receipt readiness remains experimental

Normal controller polling copies a native journal header through a synchronous controller call.
Direct readiness observes atomic request state from the polling thread.
This removes those controller calls, but it also changes the interaction between the selected thread, peer, and controller.

A readiness result is only a hint that the journal can be copied.
Failed and cancelled journals can also be ready.
The controller still copies the full receipt, checks its timing and identity, and settles valid payment.

The unrestricted controlled run passed zero speedup, treatment, and expected invalidation.
The one-CPU comparison did not establish a capacity benefit.

| Polling mode | Treatment batches before failure | Peer payments | Failure |
| --- | ---: | ---: | --- |
| Controller, first trial | 22,093 | 138 | Native journal failure |
| Direct, first trial | 1,905 | 1 | Peer debt above 2 ms |
| Direct, second trial | 1,275 | 0 | Peer debt above 2 ms |
| Controller, second trial | 12,505 | 73 | Peer debt above 2 ms |

The second direct trial completed no peer payment. Its first request remained pending when the debt gate failed.

All four zero-speedup cases passed. Both direct invalidation cases failed debt before completing their intended scenario.
All twelve records passed replay. This verifies the retained evidence, not workload success.

Direct queries reduced controller dispatch counts, but these runs made different amounts of progress before failure.
The results do not isolate overhead or identify a scheduling cause.
Controller polling remains the default. Direct readiness remains an opt-in diagnostic.
The debt and timing limits remain unchanged.

## A later capture can show later progress

The second direct treatment retained a completed native journal in its failure capture.
That capture occurred after the debt observation. It does not place payment completion before the debt gate.

The matching submit-action bracket and first-child timestamps support a narrower conclusion.
Under comparable clock-rate assumptions, the first child admission occurred 4.293347 through 6.017391 ms after the debt observation.
This rules out an already-completed peer payment at that observation under the timing model.
It does not identify what prevented earlier child admission.
Both direct invalidation cases also place first-child admission after the debt observation under this model.
Their intervals are 5.616384 through 7.285008 ms and 7.489455 through 9.548718 ms.
Both failed debt before completing the intended invalidation scenario.

The first direct treatment retained no published child in its initial failure capture.
That state can include a child still executing. Final peer batch counts also include work after the failure observation.
Neither observation alone proves that the peer was not scheduled.
Both controller treatments lack retained submit brackets, so these records cannot support a complete timing comparison between modes.

The next diagnostic needs generation-bound probe and child phases at submission and the debt gate.
Complete scheduler evidence can then distinguish lack of peer execution from execution outside probes or incomplete payment.

Native Coz checks debt on application threads and waits there when local delay trails global delay.
It does not use CozNet's managed controller receipt handoff for each peer payment.
See [the pinned native implementation](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L678-L758).
Removing a synchronous handoff changes scheduling opportunities, but these observations do not establish a scheduling cause.

See [the peer-progress analyzer](../Reference/cooperative-peer-progress.md) for the conditional timing model.

See the [managed option reference](../Reference/cooperative-managed.md#optional-direct-receipt-readiness)
and the [native query contract](../Reference/cooperative-interop.md#receipt-readiness).
