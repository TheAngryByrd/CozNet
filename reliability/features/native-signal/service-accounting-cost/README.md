# Fixed journal adapter costs

The [Windows](windows.json) and [Linux](linux.json) runs each complete 60 batches without an accounting failure.
Each run executes 120,000 measured calls plus 3,072 warmup calls.
The [manifest](manifest.json) records source and binary hashes captured after both runs.
The workload and adapter sources did not change between runs.

The workload calls the real adapter and immutable accounting model.
Fixed synthetic journals contain 1, 8, 32, or 64 measured children.
Each child contributes 1,000 ns. A terminal suffix adds one deferred child.
Each invocation starts from the same immutable state for its case.
It checks accepted count, measured wait, terminal status, and pending reservation count.

| Operation | Children | Windows median ns/call | Linux median ns/call | Windows and Linux bytes/call, first batch |
| --- | ---: | ---: | ---: | ---: |
| New prefix | 1 | 2,161.3 | 5,244.8 | 1,040 |
| New prefix | 64 | 16,859.0 | 31,320.8 | 52,424 |
| Repeated prefix | 1 | 807.9 | 963.9 | 296 |
| Repeated prefix | 64 | 310.5 | 612.9 | 776 |
| Terminal suffix | 65 | 749.6 | 1,263.2 | 2,168 |

Each elapsed value is the median of five batch means, with 2,000 calls per batch.
Raw reports retain every batch, allocation count, collection count, and checksum.
The measured interval includes assertions and loop overhead.
Setup, native calls, and JSON serialization are outside that interval.

The repeated-prefix path allocates even when the journal does not change.
Its 64-child case allocates 776 bytes per call in the first batch on both platforms.
This is a candidate for reuse of an unchanged cursor after all required checks.
New-prefix processing also allocates accounting states for each child.

The runs use normal runtime tiering and a short fixed warmup.
Later cases can run faster than smaller earlier cases.
These timings do not establish a stable scaling curve or compare operating-system performance.
Native snapshot copying, live receipt caching, sampled capacity, and prediction accuracy remain unmeasured here.

## Commands

Windows uses the existing FAKE build:

```powershell
dotnet run --project build -t ServiceAccountingCost
```

Linux uses the same built assembly in `mcr.microsoft.com/dotnet/sdk:10.0.100`:

```text
dotnet /app/CozNet.LinuxPerf.dll --service-accounting-cost /output/linux-20261014T1543.json
```

The Linux container has no network, drops all capabilities, and mounts the assembly directory read-only.
It uses a 30-second process watchdog.
See [the workload contract](../../../../lode/profiling/service-accounting-cost.md).
