# Native service startup costs

Two diagnostic runs retain the original startup capacity failure.
Both zero-speedup cases pass. Both 50-percent cases fail during warmup.
The traced treatment records 18,999,943 ns of peer debt at failure.
Targets join and native sessions close in both treatments.

The runs change observation only. They keep the 2 ms debt limit, 50 µs child request limit, and existing timing bounds.

| Capture | Original artifact directory | Retained result |
| --- | --- | --- |
| Controller phases | `20261014T162838195-2c8a41c0` | [Summary](phases/summary.json) |
| Controller phases with JIT events | `20261014T163041817-8a758747` | [Summary](jit/summary.json) |

Both original directories are under `artifacts/linux-sampled-service/`.
Each capture manifest records unchanged source and binary hashes during execution.
The decoder changed after collection because its first version did not export JIT events.
The [analysis manifest](analysis-manifest.json) records the final decoder, analyzer, and analyzed artifacts.
The analysis uses the original trace. No timing workload was repeated to repair decoding.

## Phase evidence

The phase-only treatment records 28.3221 ms of controller CPU during its first drain.
That phase spans 28.340907 ms of elapsed time, including the clock brackets.
The first peer completion records 9.4553 ms CPU within 9.456929 ms elapsed.
Scheduling delay cannot explain most of either interval.

## JIT correlation

The [decoded trace](jit/events-50.json) reports zero lost events.
Clock anchors constrain the process-clock offset to a 3,608 ns interval.
The analyzer pairs 2,721 compilation starts and method-load events on the controller thread.
It merges nested compilation intervals before summing overlaps with each phase.
Compilation intervals measure elapsed time, not compiler CPU time.

| Phase | Elapsed ms | Controller CPU ms | Guaranteed compilation overlap ms |
| --- | ---: | ---: | ---: |
| First drain | 22.804104 | 22.7665 | 21.149750 |
| First selected completion check | 2.661243 | 2.5122 | 2.602347 |
| First selected preparation | 3.570351 | 3.5692 | 3.174254 |
| First peer preparation | 4.140516 | 4.1393 | 3.779304 |
| First peer completion with a journal | 10.725568 | 10.7244 | 9.865125 |

The first drain includes compilation of `RingReader.Drain`, sample publication, `SampleCreditStreamModule.observe`, and `BigInteger` operations.
Peer completion includes compilation of `ServiceAccountingAdapter.Accept`, `NativeServiceClient.Read`, timing parsing, and ledger settlement.
See [the full correlation](jit/jit-analysis.json) for individual method intervals and conservative upper bounds.

The traces support first-use compilation as the main measured startup cost in these phases.
They do not establish steady-state capacity or prediction accuracy.
The next experiment must initialize the relevant paths before sampled debt starts accumulating.
It must preserve the original failed runs and all measurement limits.

## Checks

The phase-capture checks pass on Windows and Linux:

- `FailedPhaseRetainsCpuInterval`
- `CaptureLimitStopsClockReads`
- `RegressedCpuCannotPublishInterval`

`python native/ServiceJitChecks.py` passes three checks for nested intervals, thread filtering, clock uncertainty, and invalid trace rejection.
The eight runtime-correlation checks and three abort-correlation checks also pass.

Reproduce the correlation with:

```sh
python native/AnalyzeServiceJit.py reliability/features/native-signal/service-startup/jit/speedup-50.json reliability/features/native-signal/service-startup/jit/events-50.json artifacts/service-jit-analysis.json
```

See [the experiment guide](../../../../docsSrc/How_Tos/run-native-sampled-service.md).
