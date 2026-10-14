# Native target clock measurement

This workload measures the existing target CPU query without signals or the CLR.
It does not establish managed-runtime safety or clock accuracy.
The signal library binary matches the earlier target CPU capture.

## Method

The target registers once and remains alive until all controller reads stop.
It either runs an arithmetic loop or waits on an atomic stop flag.
For sleeping cases, the controller requires Linux task state `S` before measurement.
It records task state again before releasing the target.
The target reads its own CPU clock before readiness and after controller completion.
Those observations enclose the remote CPU observations.

Each process warms up 1,000 iterations and measures 10,000 iterations.
The remote mode calls `coz_signal_target_cpu` once per iteration.
The baseline mode omits that call.
Both modes record outer wall brackets and store readings in a preallocated vector.
File output occurs after the target joins.
Controller CPU is measured across the complete loop.
The paired CPU difference includes query execution and its additional stores.

Run `run-20261014T041852149-34c61e0d` contains three blocks of four cases.
The [evidence archive](data/target-cpu-clock/run-20261014T041852149-34c61e0d.zip) contains 63 files, verified against their retained originals.
Its SHA256 is `7be86b5854cc95d6603d4e189626514a02957813617b361e893e5356b2076c44`.
The capture snapshot and later analysis snapshot remain separate.
Seed `20261020` fixes the order within each block.
It does not fix scheduling.
Each child has a 15-second watchdog.
The collector records source hashes, binary hashes, image identity, kernel version, and all process outcomes.

```powershell
dotnet run --project build/build.fsproj -- -t LinuxTargetCpuMeasure
```

The retained capture invoked `pwsh -NoProfile -File native/MeasureLinuxTargetCpu.ps1` directly after the native build.
The later FAKE target uses the same script and defaults.

## Results

All twelve processes complete and all twelve clock analyses pass.
The 60,000 remote observations produce no decreasing counters or CPU deltas beyond their enclosing wall intervals.
All remote observations lie within the target's self CPU interval.
Each sleeping case reports `S` at both boundaries and zero target CPU advance between its first and last remote readings.
Each busy remote case records target CPU advance.

| Block | Target | Added controller CPU per reading | Remote median call | Remote p99 call | Longest remote call |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1 | Busy | 331.63 ns | 347 ns | 531 ns | 13,887 ns |
| 2 | Busy | 342.85 ns | 375 ns | 402 ns | 23,883 ns |
| 3 | Busy | 343.03 ns | 347 ns | 556 ns | 25,568 ns |
| 1 | Sleeping | 281.97 ns | 293 ns | 348 ns | 19,349 ns |
| 2 | Sleeping | 285.63 ns | 294 ns | 346 ns | 24,995 ns |
| 3 | Sleeping | 285.27 ns | 293 ns | 402 ns | 11,999 ns |

The added CPU value subtracts the matching baseline in the same block.
It is a mean over 10,000 iterations, not a per-call CPU timestamp.
Call percentiles use outer monotonic brackets around each query.
The maximum is an observed sample, not an upper latency guarantee.

## Limits and next action

The measurement covers short native loops on one Linux x64 environment.
It excludes managed dispatch, allocation, signal handling, and GC.
It does not control CPU affinity, migration, scheduler interference, or virtual-machine scheduling.
It does not reproduce the five inconsistent clock records in the earlier managed GC case.
It supplies no error bound that would justify accepting those records.

The analyzer checks physical consistency under the same exact-accounting model.
Passing this check does not prove calibrated clock accuracy.
No analyzer tolerance or timing limit changes follow from this result.

The next useful trace must retain an explicit aborted-measurement boundary when a handler limit fails.
The earlier GC trace lacks a required boundary and cannot establish suspension overlap.
Missing markers must still fail analysis.
An explicit abort record can preserve failure semantics while permitting complete clock alignment.

See [target CPU evidence](target-cpu.md), [clock ownership](../../../lode/profiling/target-cpu-observations.md), and [runtime suspension traces](../../../lode/profiling/runtime-suspension-trace.md).
