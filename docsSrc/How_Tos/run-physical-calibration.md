# How to run physical CPU calibration

Use this procedure to retain a fixed calibration of the candidate checksum optimization.
Install the repository's .NET SDK and PowerShell requirements first.
Docker must run Linux containers. The local image `mcr.microsoft.com/dotnet/sdk:10.0.100` must be available.

1. From the repository root, run the build target.

   ```powershell
   ./build.cmd LinuxPhysicalCalibration
   ```

   On Linux, use `./build.sh LinuxPhysicalCalibration`.

2. Open the printed directory under `artifacts/physical-calibration/`.
   Read `summary.json` and `outcomes.json` for process failures or missing records.
   Read `calibration.json.plan.json` for the declared trial order.
   If the process stopped early, inspect complete rows in `calibration.json.trials.jsonl` for earlier measurements.

3. Read `calibration.json` for raw measurements and the estimate.
   Retain failed and unexecuted trials.
   Do not use `calibrationComplete` as a prediction-accuracy result.

The runner saves source and binary snapshots with hashes before execution.
The source snapshot covers workload C# files and selected build inputs. It is not a complete rebuild checkout.
It checks the portable verifier on the host and Linux, then runs physical baseline, optimized, and calibration cases on Linux.
Linux cases use a 30-second process watchdog. The host verifier uses its individual worker deadlines.
The runner reports failure after it retains every declared process outcome.

See [the calibration reference](../Reference/physical-calibration.md) for formulas and measurement limits.
