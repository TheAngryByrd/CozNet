# Direct receipt readiness comparison

The portable native query observes atomic ownership and mailbox quiescence without copying journals.
Only normal selected and peer polling use it. Cleanup and receipt acceptance retain controller ownership.
The default remains controller header polling.

## Controlled checks

Windows and Linux each pass all 13 native service checks.
The target checks cover pending, executing, successful, failed, cancelled, stale, and reused generations.
The ABI check covers generation width, roles, invalid handles and slots, and release authorization.
Windows and Linux pass the managed selector checks.
The selector preserves slots, Boolean results, original failures, and attempted-call counts.
All 25 peer and 59 selected Python checks pass. Outcome-reader fixtures pass.
Both full replay entry points reject disabled readiness with a nonzero call count.
The retained live manifest predates the final test-only activation and replay integration fixtures.

These checks do not isolate the managed import from the live workload or force every workload branch.

## Live evidence

Unrestricted run: `artifacts/receipt-readiness/20261015T223422993-d6117fc0`.
All three cases pass with direct readiness enabled.

One-CPU comparison: `artifacts/receipt-readiness-single/20261015T223518168`.
The fixed order is controller, direct, direct, controller.
Each trial requests 50,000 selected batches with sample-qualified cadence and owner draining.
Per-sample service, child CPU observations, pause timing, and dispatch timing remain enabled.
The runner binds sources and binaries to the unrestricted manifest and verifies unchanged inputs after each trial.

| Trial | Treatment batches | Direct queries | Controller dispatches | Peer payments | Treatment result |
| --- | ---: | ---: | ---: | ---: | --- |
| Controller 1 | 22,093 | 0 | 164,753 | 138 | Native journal failure |
| Direct 1 | 1,905 | 13,412 | 10 | 1 | Peer debt 2,499,896 ns |
| Direct 2 | 1,275 | 9,874 | 7 | 0 | Peer debt 2,500,000 ns |
| Controller 2 | 12,505 | 0 | 96,496 | 73 | Peer debt 2,361,063 ns |

Direct trial 2 completed no peer payment. Its first request remained pending when the debt gate failed.

All four zero-speedup cases pass.
Both controller invalidation cases pass.
Both direct invalidation cases fail peer debt before completing the intended invalidation scenario.
They stop after 1,051 and 440 batches, each at 2,500,000 ns peer debt.
All twelve records pass evidence replay. Replay validity does not turn a failed workload into a pass.

## Interpretation

Direct readiness removes normal header dispatches in retained evidence.
It does not establish stable capacity. Both direct treatments stop earlier in this small comparison.
Different execution progress and observer activity prevent an isolated overhead estimate.
The comparison does not identify a scheduling cause or justify a new default.
All debt, child, and receipt limits remain unchanged.

## Accepted diagnostic limits

Mailbox quiescence can follow lease retirement. A direct query can therefore require another poll after a snapshot could report retirement.
This delay does not permit early payment or release.
Direct native query failures throw on the polling thread. Controller failures retain the worker's aggregate exception wrapper.
The selector preserves the exception from its selected path. The two paths do not promise identical exception types.
The managed import has live workload coverage, not an isolated native-error fixture.
These limits are acceptable for an opt-in diagnostic with unchanged payment validation and no general capacity claim.
Managed generation storage still requires serialized submit, query, and release operations.
The current workload supplies that ordering. The native any-thread query does not make the managed client thread-safe.
Native race safety relies on the shared lifetime guard and atomic state.
Controlled checks do not force query races with close or release/resubmit.
That coverage limit is accepted for this diagnostic. General concurrent managed use is unsupported.

See [the user explanation](../../../docsSrc/Explanations/receipt-readiness.md) and [the contract](../../../docsSrc/Reference/cooperative-interop.md#receipt-readiness).
