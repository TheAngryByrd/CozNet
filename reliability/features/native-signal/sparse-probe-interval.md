# Probe interval 8192

The native profiler and both Instrumentation and LinuxPerf workload parsers accept interval `8192`.
The bound payment runner exposes it through `-ProbeInterval 8192`. Its default remains `1024`.
The controlled checksum method has 10,001 countdown visits per invocation.
Interval 8192 therefore invokes one entry callback and one loop callback, compared with ten callbacks at interval 1024.
The countdown resets at entry and after each loop callback.

## Managed and replay checks

| Contract | Evidence |
| --- | --- |
| Two callbacks per batch, manual rejection, unsupported adjacent values | `InjectedProbeChecks.ParsePlans` through `--verify-collector` |
| Required integer interval matches the requested setting | `BoundSampledOutcomeChecks.ps1`, including an omitted field |
| Supported metadata, malformed rejection, and legacy absence | `test_probe_interval_metadata_preserves_legacy_and_rejects_invalid_values` |

The host collector group, PowerShell outcome checks, and 104 selected-payment replay checks pass.
The base sampled-payment replay suite also passes.
Replay checks metadata membership only. Runtime callback-count checks and runner setting checks supply separate evidence.
No replay success proves that arbitrary external records came from the requested instrumentation.

## Native Linux countdown

Capture: `artifacts/sparse-probe-countdown-8192`.

```powershell
pwsh -NoProfile -File native/VerifyLinuxNative.ps1 -ProbeInterval 8192 -EvidenceDirectory artifacts/sparse-probe-countdown-8192
```

Baseline, profiled, intervention, and unsupported-sampling cases all exit zero on Linux x64 with .NET 10.0.0.
The three injected cases retain exactly 100 entry callbacks across 100 short invocations.
The harness checks seven work sizes, including values immediately before, at, and after countdown expiration.
It also checks intervention during a million-iteration invocation and original-source IL mapping.
The native build log is `artifacts/sparse-probe-native-build.log`.
The shared verification targets now enumerate 8192 on both platforms, but this change has no new Windows runtime evidence.

## Bound Linux integration

Capture: `artifacts/sparse-probe-payment/20261016T061438877-45fdb05a`.

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 -ProbeInterval 8192 `
  -EvidenceDirectory artifacts/sparse-probe-payment -SelectedPayment -OwnerDrain `
  -DirectReceiptReady -FiniteMeasurement -SelectedCadence qualified-sample -SelectedBatches 50000
```

All four managed groups and all three live cases pass. Input hashes remain unchanged.
Every runtime record reports interval 8192, and the runner accepts that requested-setting match.
Completed cases verify exact selected and peer callback counts against their completed checksum batches.
The invalidation case verifies rejected resume after selected payment.

| Case | Service checks | Executions | Skips | Selected pauses |
| --- | ---: | ---: | ---: | ---: |
| Zero speedup | 100231 | 231 | 100000 | 0 |
| 50-percent speedup | 100290 | 41325 | 58965 | 31 |
| Expected invalidation | 216 | 34 | 182 | 1 |

These counters span the workload lifetime, including final draining.
The 2 ms debt and lease limits, 50 µs child requests, 5 ms handler limit, and 100 ms receipt limit remain unchanged.
Authoritative payment still uses wall time.

## Default regression

Capture `artifacts/sparse-probe-default/20261016T061614600-8dff233e` omits `-ProbeInterval`.
Its manifest retains 1024. All three runtime records pass the requested-setting check.
All four managed groups and zero-speedup, treatment, and expected-invalidation cases pass with unchanged inputs.
This validates the default path with the new profiler and metadata checks.

## Accepted limits

A backedge interval is not a time bound. Larger intervals can delay draining, receipt handling, and peer service.
The single loop callback occurs at a fixed position in each checksum batch and can change sampling and pause patterns.
A passing controlled run does not establish stable capacity or general application safety.
NativeMap and older perf, prediction, and cooperative-interop runners retain their existing interval sets.
Interval lists remain duplicated across components and require an explicit audit when another value is added.
The four-arm comparison runner still selects 1024, so this capture supplies no physical-prediction comparison at 8192.
No elapsed-time benefit or prediction accuracy is established.

See [the procedure](../../../docsSrc/How_Tos/run-bound-selected-cadence.md#run-the-lower-density-probe-experiment)
and [the interval reference](../../../docsSrc/Reference/bound-sampled-payment.md#probe-interval).
