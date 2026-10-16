# How to measure cooperative receipt cost

Use this diagnostic to compare the first managed receipt call with later calls in one fresh process.
It requires the repository's .NET SDK. It does not require a native profiler, CPU sampling, or elevated permissions.

1. Build the workload in Release mode.

   ```powershell
   dotnet build tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj -c Release
   ```

2. Create an evidence directory.

   ```powershell
   New-Item -ItemType Directory -Force artifacts/receipt-cost
   ```

3. To match the live runner's requested compilation settings, set these variables in the diagnostic shell.

   ```powershell
   $env:DOTNET_TieredCompilation = '0'
   $env:DOTNET_ReadyToRun = '0'
   ```

   These settings affect subsequent .NET processes in that shell. Use a separate shell for this diagnostic.
   The output records these variables and their `COMPlus_` counterparts. Keep all requested settings with the results.

4. Run the built workload with a new output filename.

   ```powershell
   dotnet tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll --measure-cooperative-receipt-cost artifacts/receipt-cost/first.json
   ```

5. Check `passed`, `failure`, and the 32 observation records.
   A successful process exits zero. Each observation must confirm ten children, 500,000 ns, terminal settlement, and zero remaining peer debt.

6. Compute observation time as `(Observed - Started) / timestampFrequency` seconds.
   Compute acceptance time as `(Accepted - Observed) / timestampFrequency` seconds.
   Compare iteration zero with later iterations. Keep each fresh process in a separate file.

The executable uses shared managed APIs and runs on Windows and Linux.
For other shells, create the directory with that shell's directory command.
The command replaces an existing output file, so use a new filename for each capture.

See [the diagnostic fields](../Reference/cooperative-managed.md#controlled-receipt-cost-diagnostic)
and [the interpretation limits](../Explanations/cooperative-pause-cost.md#controlled-managed-receipt-cost).
