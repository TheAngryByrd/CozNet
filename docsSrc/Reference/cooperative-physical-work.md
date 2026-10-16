# Cooperative physical work

The experimental physical route uses the same checksum work as the bound sampled workload.
Controlled baseline and optimized runs complete on Windows x64 and Linux x64 with 50,000 selected batches each.
The first matched Linux block completes and remains unqualified.

## Command

```text
--cooperative-physical <output.json> <baseline|optimized> <batches>
```

The command belongs to `tests/CozNet.LinuxPerf`.
`batches` accepts a positive decimal integer within the `Int32` range.
`baseline` uses original selected arithmetic. `optimized` changes only selected arithmetic.
The peer always uses original arithmetic.

The route requires `DOTNET_TieredCompilation=0` and `DOTNET_ReadyToRun=0`.
It rejects `CORECLR_ENABLE_PROFILING=1` or `DOTNET_ENABLE_PROFILING=1`.
The implementation uses managed runtime APIs.
The retained platform checks use .NET 10.0.12 on Windows and .NET 10.0.0 on Linux.
Other runtime and platform combinations remain unverified.

## Work contract

`workloadProtocol` is `cooperative-checksum-v1`.
Each batch must produce checksum `49995000` before it publishes progress.
Both owners execute original and optimized arithmetic during warmup.
The caller executes selected work. A dedicated worker executes peer work.
The peer starts before measurement capture.

The end boundary precedes the peer stop request and worker join.
The frozen progress counters define measured work. Final peer totals can include work outside those boundaries.
Completion requires all requested selected batches and positive peer progress within the measurement boundaries.

## Output

| Field | Meaning |
| --- | --- |
| `workloadProtocol`, `mode`, `requestedBatches` | Work identity and requested selected work. |
| `completed`, `failures`, `joined` | Completion, retained failures, and worker cleanup result. |
| `measurementWindow.Start`, `measurementWindow.End` | Clock brackets and frozen progress readings. |
| `measurementWindow.Status` | Numeric `MeasurementCaptureStatus` value. `Complete` is `2`. |
| `selectedWorkStarted`, `selectedWorkFinished` | Additional selected-loop timestamps, when available. |
| `selectedBatches`, `checksum` | Final selected progress and checksum total. |
| `peerBatches`, `peerChecksum` | Final peer progress and checksum total. |
| `expectedChecksum` | Required result per batch. |
| `timestampFrequency` | Stopwatch ticks per second. |
| `runtime`, `os`, `architecture` | Runtime and platform descriptions. |
| `predictionAccuracyEstablished` | Always `false`. |

The command returns zero only when work and cleanup complete without retained failures.
Argument parsing and output-write failures can prevent creation of the result file.

See [the comparison explanation](../Explanations/cooperative-physical-comparison.md)
and [finite cooperative measurement](cooperative-measurement.md).
