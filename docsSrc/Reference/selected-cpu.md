# Selected-thread CPU capture

`VerifyCooperativeComparison.ps1 -SelectedCpu` enables CPU observations in all four fixed-work arms.
The setting defaults to disabled. The command uses Linux thread CPU time.
The shared capture accepts `IThreadCpuClock`, so other platforms can supply a clock without changing the capture logic.

## Activation

`COZNET_SELECTED_CPU` accepts `1`, `0`, or absence. Other values reject.
Enabled sampled capture requires finite fixed selected work and an available clock factory.
The clock receives a preparation read before measurement. That read does not enter the result.

Plans bind the Boolean `selectedCpu` with `selectedCpuEvidenceVersion=1`.
Every arm must report matching activation. Missing or mismatched evidence invalidates the arm and suppresses the aggregate comparison.
Legacy plans permit disabled capture only.

## Record fields

| Field | Meaning |
| --- | --- |
| `selectedCpuEnabled` | Boolean activation |
| `selectedCpuEvidenceVersion` | `1` |
| `selectedCpu` | Capture object, or null when disabled |
| `Status` | `prepared`, `running`, `complete`, or `failed` |
| `StartReading`, `EndReading` | CPU observations, or null if not reached |
| `Failure` | First capture error, or null |
| Reading `CpuNanoseconds` | Cumulative CPU time on the selected thread |
| Reading `BeforeTimestamp`, `AfterTimestamp` | Managed timestamps around the CPU clock read |

The capture starts after `selectedWorkStarted` and ends before `selectedWorkFinished`.
Each operation requires the creating managed thread. Invalid transitions and CPU regressions invalidate capture permanently.
Foreign-thread calls reject before changing state or reading the clock.
Completed work requires a complete capture. Failed workloads can retain partial observations.

## Analysis

Each versioned comparison case contains `selectedCpu` with CPU advance and elapsed bounds, or null when unavailable.
CPU advance is `end.CpuNanoseconds - start.CpuNanoseconds`.
The elapsed lower bound uses `end.BeforeTimestamp - start.AfterTimestamp`.
The upper bound uses `end.AfterTimestamp - start.BeforeTimestamp`.
The analyzer converts wall ticks with `timestampFrequency` and rounds bounds outward to nanoseconds.
It never subtracts absolute CPU timestamps from wall timestamps.

These observations include profiler work, selected payment CPU, and clock-read cost within the endpoints.
They do not change credit, payment, or effective elapsed time.
The capture prepares the clock API. It does not claim to remove all first-use runtime cost.

See [the explanation](../Explanations/selected-cpu.md) and [capture procedure](../How_Tos/capture-selected-cpu.md).
