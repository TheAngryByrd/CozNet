# Matched CPU workers

`MatchedCpuWorkers` owns the shared release gate, stop flag, readiness gates, checksum loop, and two progress counters.
The sampled timer and matched physical routes use the same worker implementation.
Each batch checks `49995000` before publishing progress.
`PhysicalWorkMode` parses baseline or optimized selection before any worker starts.
Only the selected worker changes arithmetic implementation in optimized mode.
Both methods run once before worker creation in both routes.

```powershell
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_ReadyToRun = '0'
dotnet tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll --timer-physical baseline.json baseline
```

The physical controller sleeps through a two-second measurement window.
It captures the shared immutable boundary before requesting stop and joining both workers.
All physical delay and wait counters remain zero.
Failures remain in JSON and force a nonzero process exit.
The physical path uses no native adapter. Runtime checks cover Windows x64 and Linux x64 only.

The native timer caller still owns registration and retirement.
It disposes readiness gates only after joins, and the release gate only after native session close is attempted safely.
The physical caller retains gates when a worker does not join.

```mermaid
flowchart LR
    Warmup[Prepare both arithmetic methods] --> Ready[Two dedicated workers at gate]
    Ready --> Start[Capture and release]
    Start --> Work[Shared checksum and progress loop]
    Work --> End[Freeze boundary]
    End --> Cleanup[Stop and join]
```

Calibration and prediction analysis remain absent. No nominal 50-percent accuracy claim follows from the optimized implementation.
The older physical route remains separate for previous experiment scripts.
See [the reference](../../docsSrc/Reference/matched-physical.md), [boundaries](measurement-boundaries.md), and [sampled timers](timer-sampled.md).
