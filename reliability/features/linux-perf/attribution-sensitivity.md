# Raw IL attribution sensitivity

Candidate exclusion does not fix prediction accuracy in the controlled countdown workload.
Both retained runs fail the unchanged two-percentage-point gate.
All 64 workload processes pass their applicable checks.
The 32 profile cases pass checksum, probe-count, sample-accounting, and live-intervention checks.
The 32 physical cases pass progress and checksum checks.

| Diagnostic mode | Predicted reduction | Physical reduction | Mean error | 95% interval half-width | Gate value | Profiling overhead |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Whole method | 43.63% | 49.40% | -5.76 points | 0.93 points | 6.69 points | 26.12% |
| Exclude candidates | 41.99% | 49.70% | -7.71 points | 1.25 points | 8.96 points | 24.96% |

The gate value is `abs(mean error) + interval half-width`. It must not exceed two percentage points.
The error interval uses eight paired blocks and Student's t critical value `2.364624251`.
These are separate diagnostic runs, not randomized comparisons between attribution modes.
Their difference does not isolate the causal effect of exclusion from other run-level changes.

## Protocol

Both modes use interval 1024, raw CLR maps, and empty-stack gate placement.
Tiered compilation and ReadyToRun remain disabled.
Each mode runs eight shuffled blocks with seed `20261013` and two-second measurement windows.
Each block contains zero-speedup and 50-percent profiles plus unprofiled baseline and optimized cases.
The optimization replaces half the additions with an equivalent constant sum.
Prediction subtracts live global delay from measured elapsed time. It does not subtract profiler service time.

The first mode retains whole-method credits.
The second assigns a distinct symbol to native ranges whose mapped IL instruction is absent from the private original-instruction map.
Those samples retain their periods and instruction addresses but earn no selected-method credit.
Special CLR ranges retain whole-method treatment. Unmapped samples remain separate.

The map must partition the native method without overlaps or gaps.
Every non-special mapping offset must identify a decoded rewritten instruction.
The diagnostic rejects a sampled address whose selected-method identity differs from the captured range.
Every record keeps `nativeOwnershipEstablished=false`.
This experiment uses candidate ranges, not a general CLR ownership contract.

Mean candidate-period fractions within the native method are 5.05% and 5.48% for the whole-method control and treatment.
They are 5.12% and 4.90% for the filtered control and treatment.
These fractions sum sample periods by native range. They are not direct CPU-clock measurements.
The raw records retain code bytes and JIT disassembly for independent inspection.

## Evidence and commands

[Whole-method records](data/attribution/raw/) and [filtered records](data/attribution/filtered/) retain all cases, order, environment, summaries, and profile disassembly.
The `profile-*-candidates.json` files sum candidate and whole-method periods for each profile case.
Each fraction is `Candidates / WholeMethod`. The table above uses each runner's `summary.json`.

```powershell
dotnet run --project build/build.fsproj -- -t LinuxRawAttributionVerify
dotnet run --project build/build.fsproj -- -t LinuxFilteredAttributionVerify
```

Both commands fail after writing their accuracy summaries. This failure is the expected result for the retained runs.
The Linux binaries run in Linux x64 containers from a Windows host.
The shared range checks also run on Windows without native profiler calls.

| Requirement | Evidence |
| --- | --- |
| Reject incompatible diagnostic settings | `RawIlCandidateChecks.ParseModes` |
| Use half-open ranges and preserve other methods and special ranges | `RawIlCandidateChecks.HalfOpenRangesPreserveSpecialAndOtherMethods` |
| Reject gaps, overlaps, invalid IL offsets, truncation, and address overflow | `RawIlCandidateChecks.RejectMalformedMaps` |
| Reject a changed selected-method range | `RawIlCandidateChecks.RejectChangedMethod` |

Run the portable checks with `dotnet tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll --verify-collector` after a Release build.

## Decision

Keep candidate exclusion diagnostic. Do not enable it in the default sampled path.
This result does not justify an accuracy correction or a relaxed gate.
Removing candidate credits leaves their execution cost in the experiment duration.
Measure probe, collection, and intervention costs separately before changing the estimator or choosing another intervention mechanism.
The present tiny loop remains an unresolved overhead case.

See [native mapping evidence](../runtime-probes/native-mapping.md), [countdown prediction](countdown-prediction.md), and [overhead accounting](../../../docsSrc/Research/profiler-overhead-accounting.md).
