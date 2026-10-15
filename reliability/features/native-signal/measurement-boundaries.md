# Measurement boundary evidence

This increment retains controller-accounted measurement state separately from shutdown state.
It does not establish sampled prediction accuracy or assign unreceived samples and journal tails to the measurement window.

## Portable checks

Seven checks run through `--verify-measurement-boundaries` and the existing `--verify-timer-layout` entry point.

| Check | Required behavior |
| --- | --- |
| `FrozenEndExcludesShutdownChanges` | Both worker readings, sample identity, pending ownership, and clock brackets remain unchanged after later state changes. |
| `ReleaseOccursInsideValidatedStartBracket` | Initial readings precede release, and the start clock bracket contains release. |
| `FailedFinishCannotCaptureShutdownState` | Read and closing-clock failures preserve the first failure and cannot trigger a later capture. |
| `InvalidReadingsPreventRelease` | Negative counters and invalid pending ownership fail before release. |
| `ClockRegressionCannotPublishBoundary` | Backward clocks and unusable frequency cannot produce valid boundaries. |
| `InvalidTransitionsDoNotInvokeCallbacks` | Finish before start and retry after failed start cannot execute capture callbacks. |
| `CumulativeCountersCannotRegress` | Global delay, credit, sample identity, progress, and released waits cannot decrease within a window. |

The Release checks pass on Windows and in `mcr.microsoft.com/dotnet/sdk:10.0.100` on Linux.
Linux uses the Release output mounted read-only and runs the full layout verifier, including these checks.
The console verifier emits no case counts. The count above comes from its explicit check registrations.
Logs under `artifacts/measurement-boundaries/` retain build and verifier exit codes.

```powershell
dotnet build tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj -c Release --no-restore
dotnet tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll --verify-measurement-boundaries
dotnet tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll --verify-timer-layout
```

## First live integration

`artifacts/timer-sampled/20261015T000328395-d5ce8aeb` retains the first `LinuxFastTimerCapacity` comparison with boundary capture.
Zero percent passes. Treatment fails the unchanged 2 ms debt gate at 2,499,953 ns of peer debt.
Both captures complete, and both sessions close without pending ownership.
The treatment endpoint retains peer command 2,125 and generation 2,076 before cleanup releases them.

This run exposed a timing placement issue: the duration clock started before capture setup.
Setup therefore shortened the requested live window.
The clock now starts inside the release callback, after sampler enablement and before the nominal start timestamp and worker release.
The first run remains retained. The next run verifies this code change.

## Live integration with the release clock

`artifacts/timer-sampled/20261015T000620519-d3025546` retains the comparison after that timing change.
The manifest records the new capture source files. The summary reports unchanged inputs.
The layout verifier and zero-percent case pass. Treatment fails the unchanged debt gate.

| Observation | Zero percent | 50 percent |
| --- | ---: | ---: |
| Nominal elapsed seconds | 2.000001650 | 0.231867273 |
| Start bracket width, ns | 2,342,209 | 3,632,636 |
| End bracket width, ns | 5,450 | 134,321 |
| Frozen selected progress | 592,878 | 68,333 |
| Final selected progress | 597,101 | 70,162 |
| Frozen peer progress | 598,182 | 31,405 |
| Final peer progress | 602,363 | 34,184 |
| Peak peer debt, ns | 0 | 2,499,995 |

Both `measurementCapture.status` values are `Complete`. This describes capture completion, not experiment validity.
Treatment retains peer command 268 and generation 268 at its endpoint.
Both sessions later close with zero pending ownership. Final progress includes work after the frozen endpoint.

`artifacts/measurement-boundaries/analyze.py` verifies both retained runs without executing another workload.
It checks clock ordering, accepted sample counts and credit, legacy-field agreement, counter ordering, and retained pending identities.
Each run's `boundary-analysis.json` records input hashes, computed values, and the failed experiment outcome.
The analysis does not convert a debt failure into a pass.

## Remaining limits

The start brackets include setup and release work. Their widths are retained uncertainty, not measured scheduler delay.
The reads are sequential. They do not provide an atomic view of both workers and native handlers.
Pending and unreceived native work still needs a terminal attribution rule before prediction analysis.
The physical workload does not yet share these boundaries or the timer workload's worker layout.
The two-percentage-point accuracy gate remains unmet.

See [the boundary contract](../../../docsSrc/Reference/measurement-boundaries.md) and [the sampled timer reference](../../../docsSrc/Reference/sampled-timer.md).
