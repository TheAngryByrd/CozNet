# Native service interop and first managed receipt

The C ABI and source-generated .NET client pass controlled receipt and teardown checks on Linux x64.
The receipt parser also passes on Windows.
These results do not establish sampled service capacity or physical prediction accuracy.

## Runs

Both runs remain available:

| Run | Native checks | Normal receipt upper bound | Late receipt upper bound | Late fixture |
| --- | --- | ---: | ---: | --- |
| [Initial](20261014T065657123-166f77b4/summary.json) | 6/6 passed | 541,451 ns | 150,842,679 ns | Rejected as required |
| [Revised](20261014T070014677-3a375853/summary.json) | 6/6 passed | 481,104 ns | 150,874,296 ns | Rejected as required |

The revised fixture also requires a fresh native snapshot clock more than 100 ms after the first receipt.
It verifies that this later snapshot preserves the child's original receipt bounds.
The revised driver checks source hashes across compilation and execution, and binary hashes across execution.
The revised C header also distinguishes Windows import and export declarations for a future backend.
The initial run passed before these changes. Neither run replaces a failed timing outcome.

The [revised manifest](20261014T070014677-3a375853/manifest.json) records image, source, and binary hashes.
The [native log](20261014T070014677-3a375853/native-checks.log) contains five portable owner cases and one ABI case.
The [normal observation](20261014T070014677-3a375853/receipt.json) and [late observation](20261014T070014677-3a375853/late.json) retain all child timing fields.
The command is:

```powershell
dotnet run --project build -t LinuxServiceInteropVerify
```

## ABI checks

`ApiChecks.cpp` verifies:

- Invalid configuration does not consume installation ownership.
- A second active session fails.
- A target thread cannot perform controller clock or close operations.
- Insufficient snapshot buffers do not authorize release.
- A complete snapshot preserves surrounding buffer guards.
- A live registered target prevents close after lease release.
- Retirement and join permit close.
- Stale handles fail without accessing the closed session.

## Managed receipt checks

The target is a registered managed thread with an ineligible instruction range.
Its signal acknowledgment is deferred and performs no injected wait.
The caller reads the native monotonic clock after the snapshot returns to managed code.
That reading bounds receipt from above in the child's original clock domain.

In the revised normal case, native acknowledgment takes 39,517 ns.
The first managed receipt upper bound is 481,104 ns.
The client retains that bound after another 150 ms and a fresh snapshot.

In the revised late case, native acknowledgment takes 23,109 ns.
The fixture waits 150 ms before its first managed read.
The resulting receipt upper bound is 150,874,296 ns, which fails the unchanged 100,000,000 ns gate.
The fixture passes only because this rejection is expected.
It does not turn that late receipt into an accepted profiling observation.

Both fixtures release terminal ownership, retire and join the target, and close the service.
The shared parser checks the exact limit, invalid clock order, failed clock reads, and large absolute timestamps.
Its Windows and Linux invocations pass.
The Windows portable owner checks also pass 5/5 with the Linux ABI disabled.

## Limits

The fixtures perform no measured wait in managed CPU code.
They contain no sample credit, GC stress, or physical-change comparison.
Snapshot allocation and copy costs remain unmeasured.
An upper bound above 100 ms fails the conservative gate but does not establish the exact receipt instant.
Finalization cannot prove thread joins or satisfy controller affinity. Explicit close remains required.

See [the interop contract](../../../../lode/profiling/service-interop.md) and [the debt plan](../../../../lode/plans/native-debt-service.md).
