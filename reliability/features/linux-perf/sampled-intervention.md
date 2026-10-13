# Linux sampled intervention

The controlled workload connects real Linux CPU events to the existing cooperative delay runtime.
This report records source commit `74bbd8c`.
See [wait comparison](wait-comparison.md) for the later CPU cadence and separate accounting measurements.
The runner uses the same .NET 10.0.0 Linux x64 container and kernel recorded in `data/environment.json`.
Each process has a 30-second limit. Each busy phase runs for approximately 500 milliseconds before final settlement.

```powershell
dotnet run --project build/build.fsproj -- -t LinuxPerfVerify
```

The event sink retains every checked event, including its timestamp, period, thread, address, and method.
The target consumes queued events at a manual probe after each arithmetic batch.
Selected events earn `period × requested percentage` credit with exact integer conversion and retained fractions.
Other and unmapped events advance the stream without earning credit.
The target's probe publishes its local delay. A second registered thread pays that delay at its own manual probes.
The collector joins before the final queue flush. Both registered threads settle before exit.

| Observation | 50 percent | Zero percent |
| --- | ---: | ---: |
| Samples delivered before disable | 319 | 487 |
| Samples applied before disable | 319 | 487 |
| Credit applied before disable | 134 ms | 0 ms |
| Worker waits before disable | 297.662286 ms | 0 ms |
| Final samples | 319 | 488 |
| Selected samples | 268 | 433 |
| Unmapped samples | 49 | 51 |
| Nominal selected periods | 268 ms | 433 ms |
| Final credit | 134 ms | 0 ms |
| Measured worker wait calls | 300.977421 ms | 0 ms |
| Global delay after settlement | 302.522154 ms | 0 ms |

The remaining two treatment samples and four control samples mapped to other methods.
Both sleep phases had zero samples. Neither run lost events or reported throttling.
Raw records and counters are in `data/intervention-50.json` and `data/intervention-0.json`.
The checks require exact credit conversion, nonzero work and probes, sufficient measured worker waits, and exact zero-control delay.
The measured waits exclude probe computation. They include scheduler delay while the wait call is active.

The first integration omitted the selected thread's live probe.
It accumulated 247.5 ms of credit but recorded only 50.673948 ms in worker checks.
Its earlier assertions passed without proving that the worker paid the credit.
`data/missing-live-probe.json` preserves that invalid integration result.
The current checks measure wait calls directly and reject waits below credited duration.
They also require applied samples, positive treatment credit, and positive worker waits before the event is disabled.
Final settlement cannot satisfy this live-intervention assertion.

An isolated mutation removed `intervention?.Probe();` from the busy loop but retained the final `Complete()` probe.
The mutant built in Release and ran in the same Linux container configuration at 50 percent.
It exited unsuccessfully with this assertion failure:

```text
Unhandled exception. System.InvalidOperationException: No sampled credit and worker wait were observed before sampling stopped.
```

| Source before mutation | SHA256 after LF normalization |
| --- | --- |
| `Program.cs` | `0EC378981C425CF3577FE0EA80F9DDA7D42AC2EA6EFFE3B1C420049796EEC9F1` |
| `SampledIntervention.cs` | `471574DCDBDB19580B7C28A8CF7454310B22885EC6CE06BB5984F959BF0BA398` |

The mutant command was `dotnet /work/CozNet.LinuxPerf.dll /tmp/mutant.json 50` with a 30-second timeout.
Verdict: detected. This check covers the missing live probe, not all possible intervention failures.

These results prove integration under the controlled workload assumptions. They do not prove accurate causal predictions.
Overshoot more than doubles nominal credit in the treatment run and requires further investigation.
The fixed selected method has no explicit blocking or asynchronous work.
The bridge uses a fixed eligibility epoch and estimates CPU duration from emitted sample periods.
General lifecycle qualification, GC transitions, method replacement, and automatic probe insertion remain unsupported here.
No source-line attribution is available. The public serial engine remains separate.

See [the backend contract](../../../lode/profiling/linux-perf.md) and [credit conversion](../../../lode/profiling/sample-credits.md).
