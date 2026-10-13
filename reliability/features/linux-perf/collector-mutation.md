# Final-drain mutation evidence

The portable collector checks passed on Windows x64 with SDK 10.0.112 and runtime 10.0.12.
They also passed inside the Linux workload on runtime 10.0.0.
The source baseline is `ea33664` plus the live collector changes.

| Source file | SHA256 before mutation |
| --- | --- |
| `LiveSampleCollector.cs` | `EFEE8D1B1AC945BB40CB3550D4939AA417B25BF02AB9E34F8136C8C90E6A76F3` |
| `CollectorChecks.cs` | `47FAA936374E431EC40802CF0863C7C70E58628F8EA0EBA22AA2DAE4331CB94D` |

The isolated copy removed the `Volatile.Write(ref latest, drain());` call after the collector loop.
The loop's drain call remained unchanged. The production source retained both calls.
The mutant built successfully in Release before execution.

```powershell
dotnet build lode/tmp/live-collector-mutant/CozNet.LinuxPerf.csproj -c Release --nologo
dotnet lode/tmp/live-collector-mutant/bin/Release/net10.0/CozNet.LinuxPerf.dll --verify-collector
```

The mutant process exited with `-532462766` and this assertion failure:

```text
Unhandled exception. System.Exception: CompletionPublishesFinalDrain: completion did not publish the final drain.
```

Verdict: detected. The check blocks the first drain until after the stop request.
Without the final drain, only one call completes and the required second snapshot is absent.
This result covers one shutdown defect. It does not prove all collector interleavings or Linux ring behavior.

See [live workload evidence](README.md).
