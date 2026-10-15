# Frozen timer prefix evidence

## Contract checks

For this retained capture, the portable `--verify-timer-layout` route passed on Windows and Linux x64.
At that time, it included nineteen controller groups and nine measurement-boundary groups.
The new checks retain an accepted 50,000 ns prefix through cancellation and release, then inspect its serialized boundary.
They also retain failed submission and release ownership.
An additional check rejects a changed native child and preserves the older accepted capture timestamp and prefix.
Boundary checks reject inconsistent worker totals, mismatched pending identities, journal-count regression, and changed timer capability.

## Native capture

The command `./build.cmd LinuxFastTimerCapacity` retained run `20261015T044443270-12d19aae` under `artifacts/timer-sampled/`.
The layout and zero-percent cases exit zero. The fifty-percent case exits one.
The summary reports `inputsUnchanged = true` and `passed = false`.
The source inventory includes the controller, boundary capture, producer, and their checks.
The rejected-observation check was added after this live run. Production code did not change.

The treatment retains `Sampled timer debt exceeded 2000000 ns.`
Its selected peak debt is 372,187 ns. Its peer peak debt is 2,499,904 ns.
Native cleanup completes with `closed = true` and no pending final journals.
The timing, lease, and debt limits remain unchanged.

| Observation | Frozen treatment end | After shutdown |
| --- | ---: | ---: |
| Archived journal count | 1,885 | 1,886 |
| Selected released wait | 1,918 ns | 1,918 ns |
| Peer released wait | 812,502,014 ns | 812,502,014 ns |
| Pending peer command | 1,886 | Released |
| Pending peer generation | 1,867 | Same generation |
| Accepted children for that command | 0 | 1 |
| Confirmed wait for that command | 0 ns | 0 ns |

The frozen record retains the last accepted empty native capture at `387369683153024` ns.
Its settlement, cancellation, and release-attempt flags are false.
The final journal is settled and released. Its one child is deferred and earns no wait.
Summing accepted measured waits in the first 1,885 archived journals reproduces both frozen released-wait totals.
The zero-percent boundaries retain empty timer inventories and zero wait totals.

These observations verify capture and preservation of controller knowledge.
They do not bound unreceived native waits or establish sampled capacity, general CLR safety, or prediction accuracy.
No unchanged timing case was repeated after this failure.

## Prefix reconciliation

The retained captures reconcile through `native/AnalyzeTimerPrefixes.py`.
Reports are `timer-prefix-reviewed-0.json` and `timer-prefix-reviewed-50.json` in the same artifact directory.
Each report records the input hash and analyzer source hashes.
These reports were produced after collection and are not part of the original capture manifest.

The treatment reports accepted wait deltas of 1,918 ns for selected and 812,502,014 ns for peer.
The peer suffix contains one later accepted child with zero wait.
That child's recorded finish precedes the endpoint bracket. Later acceptance does not mean later execution.
The report preserves the original debt failure and keeps all eligibility, native-tail, and prediction flags false.

Nineteen focused Python checks pass on Windows and Linux.
They cover frozen accounting, unknown observations, ownership regression, malformed records, closure, failure preservation, and CLI output protection.
Thirty-one matched-analysis checks pass on both platforms.
Invalid prefixes suppress the affected block. Physical cases remain exempt, and failed profile cases retain prefix diagnostics.

See [measurement boundaries](../../../docsSrc/Reference/measurement-boundaries.md),
[the controller reference](../../../docsSrc/Reference/timer-service-controller.md), and
[timer accounting](../../../lode/profiling/timer-accounting.md).
