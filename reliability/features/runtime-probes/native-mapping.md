# CLR mapping diagnostic

The diagnostic passes all eight cases on Windows x64 and all eight cases on Linux x64.
Each platform compares two controlled methods, two map modes, and two countdown placements at interval 1024.
Every case returns checksum `49995000` and executes exactly ten helper calls.
The checks also require valid private maps, bounded native ranges, rejected out-of-range reads, and JIT disassembly.
Raw mode rejects sampling because the sampler requires original IL offsets.

Run the platform target:

```powershell
dotnet run --project build/build.fsproj -- -t NativeMapVerify
dotnet run --project build/build.fsproj -- -t NativeLinuxMapVerify
```

The Linux target uses a container. These runs verify Linux binaries from a Windows host.
They do not verify the host scripts on Linux or macOS.
Records remain in [data/native-mapping](data/native-mapping/), separated by platform.
Windows uses .NET 10.0.12. Linux uses .NET 10.0.0.
The records contain runtime identity, module identity, IL bytes, native bytes, and CLR map entries.
Each platform directory contains the profiler hash. Linux also records the container image identity.

## Interpretation

Raw mapping alone does not separate application code from injected code.
For `CpuWorkload.Run`, the default placement maps rewritten IL offset 30 to a range that contains the countdown and helper call.
That IL instruction belongs to the original method.
Another range starts at injected IL offset 57 and contains the original loop comparison.

| Platform | IL 30 native range | IL 57 native range |
| --- | --- | --- |
| Windows x64 | `[32,42)` | `[42,55)` |
| Linux x64 | `[40,50)` | `[50,64)` |

Empty-stack placement moves the gate before the comparison operands at original IL offset 16.
The inspected disassembly then agrees with the separate loop ranges below.

| Platform | Decrement and branch | Helper call | Countdown reset | Original comparison |
| --- | --- | --- | --- | --- |
| Windows x64 | `[32,36)` | `[36,42)` | `[42,47)` | `[47,55)` |
| Linux x64 | `[40,44)` | `[44,50)` | `[50,55)` | `[55,64)` |

These ranges establish a useful result for this loop and these runtimes.
They do not establish a general CLR ownership contract.
Every record keeps `nativeOwnershipEstablished=false` and each range keeps `nativeOwnership="unverified"`.
The diagnostic makes no debugger-stepping claim.
It does not change sampled credit accounting or the failed prediction accuracy result.

See [CLR mapping research](../../../docsSrc/Research/injected-cpu-attribution.md) and [profiler overhead accounting](../../../docsSrc/Research/profiler-overhead-accounting.md).
