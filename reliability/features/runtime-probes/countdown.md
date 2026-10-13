# Conditional loop probes

Source baseline: `85e1bbe`, plus the countdown changes in this commit.
The shared native rewriter passes controlled checks on Windows x64 and Linux x64.
This result establishes probe coverage and delay settlement. It does not establish useful profiling overhead or prediction accuracy.

## Mechanism

`COZNET_PROBE_INTERVAL` accepts `1`, `64`, `256`, and `1024`. Missing input selects `1`, which retains dense calls.
Other values add a local countdown at each backward branch. Expiration calls the helper and resets the countdown.
Each method entry initializes the countdown and calls the helper.
Original branch targets bypass entry initialization. Targets at a backward branch pass through its countdown.

The interval counts executed poll sites. It does not bound CPU time or elapsed time between service points.
The current countdown supports only `CozNet.Instrumentation.Workload.Work`.
The native profiler rejects countdown selection with the Linux perf target through `E_NOTIMPL`.
The local-signature helper cannot distinguish failure from the first local index. The prototype rejects that ambiguous result.

## Checks

Run the platform target:

```powershell
dotnet run --project build/build.fsproj -- -t NativeCountdownVerify
dotnet run --project build/build.fsproj -- -t NativeLinuxCountdownVerify
```

Each target runs intervals 64, 256, and 1024.
`Workload.Main` checks arithmetic and exact helper counts before, at, and after the first expiration, and at the second expiration.
It also checks 100 empty invocations and one million loop iterations.
`VerifyIntervention` requires service at helper call 10 before the long invocation returns.
Its zero-credit case produces no global delay. Its 20-millisecond case must settle the known credit.
Windows checks also resolve loop stack samples through the original IL map and portable PDB.
Linux checks require explicit rejection of unsupported stack snapshots.
The default dense checks also pass on both platforms.

| Interval | Helpers per million iterations | Helpers in 100 empty calls |
| --- | ---: | ---: |
| 64 | 15,626 | 100 |
| 256 | 3,907 | 100 |
| 1024 | 977 | 100 |

Both platforms produced these counts. The dense version executes 1,000,002 helpers per million iterations.
The loop executes its backward conditional branch once more than its body.
Thus, the expected count is `1 + floor((iterations + 1) / interval)`.

Windows used .NET 10.0.12. Linux used .NET 10.0.0 in `mcr.microsoft.com/dotnet/sdk:10.0.100`.
The native build uses runtime revision `60629d14374c56f1cb51819049ad1fa529307f8d`.
[verification.zip](data/countdown/verification.zip) retains each platform result, Linux environment records, and Linux disassembly at interval 256.
[summary.json](data/countdown/summary.json) records the count comparison.

## Generated code and limits

Linux x64 generated 78 bytes for `Work` at interval 256.
The fast path decrements `r15d` and branches past `Probe` while the value is nonzero.
That path contains no managed helper call, thread-local lookup, delegate call, or clock read.
Method entry and countdown expiration call `Probe`.

This code shape supports a lower-cost experiment. It does not measure its cost.
The countdown instructions remain inside the selected method's native range.
Method-level sample filtering cannot distinguish that profiler work from application work.
The next experiment must measure overhead and retain this attribution limit.
Other architectures, macOS, arbitrary methods, exception handlers, switches, and JIT lifecycle changes remain unverified.

See [probe-overhead research](../../../docsSrc/Research/probe-overhead-options.md) and [runtime contracts](../../../lode/profiling/runtime-probes.md).
