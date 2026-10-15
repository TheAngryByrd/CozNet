# How to compare bound selected cadence

Prepare the Linux compiler and SDK container images used by the [selected-payment diagnostic](run-bound-selected-payment.md).
Install Python 3 for evidence replay.

1. Run the comparison:

   ```powershell
   dotnet run --project build/build.fsproj -- -t LinuxBoundSelectedCadence
   ```

2. Open `summary.json` in the reported `artifacts/bound-selected-cadence/` directory.
3. Inspect all four child runs, including failed runs.
4. Compare `0.json` and `50.json` across the continuous and qualified modes.
5. Check each invalidation record for rejected resume after selected payment.

The fixed order is continuous, qualified, qualified, continuous.
Each completed case executes 5,000 selected batches.
Each child run retains source copies, binaries' hashes, runtime evidence, and analysis results.
Failures do not stop the remaining comparison runs.
Do not repeat an unchanged failed comparison solely to obtain a pass.

Use completed records to compare selected work duration, sample coverage, pauses, receipts, and peer progress.
Keep failed capacity and reach outcomes beside those measurements.
The comparison does not isolate each observer cost or establish prediction accuracy.
See [the reference](../Reference/bound-selected-cadence.md) for admission and schema contracts.
