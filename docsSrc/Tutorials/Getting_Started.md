# Run the serial experiment

Install the .NET 10 SDK. Open the repository root.

```sh
dotnet run --project samples/CozNet.Serial -c Release -- artifacts/serial
```

Read `artifacts/serial/measurements.json` for observations and real-change comparisons.
Exit code `1` means the run exceeded its proposed accuracy threshold.
The [README](../../README.md) describes supported workloads and limits.
