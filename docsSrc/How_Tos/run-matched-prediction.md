# How to collect matched prediction diagnostics

Use this procedure to collect one declared comparison of sampled timers and a real arithmetic optimization.
The current analyzer cannot establish prediction accuracy and returns failure after retaining the diagnostic result.

Install the repository SDK, PowerShell, Python 3.9 or later, and Docker with Linux containers.
The local images `coznet-native-build:10.0.100` and `mcr.microsoft.com/dotnet/sdk:10.0.100` must be available.
Select a retained [interleaved calibration](run-interleaved-calibration.md) directory before collection.

1. Set the calibration directory in PowerShell.

   ```powershell
   $env:COZNET_CALIBRATION_DIRECTORY = '/absolute/path/to/retained/calibration'
   ```

2. If your Python command is `python3`, set `COZNET_PYTHON` to `python3`.

3. Run the target from the repository root.

   ```powershell
   ./build.cmd LinuxMatchedPrediction
   ```

   On Linux, use `./build.sh LinuxMatchedPrediction` with the same environment variables.

4. Open the printed directory under `artifacts/matched-prediction/`.
   Read `runner-errors.json`, `outcomes.json`, and `analysis.json`.
   Inspect each rejected case's reasons and retained log.

5. Retain the full directory. Do not repeat unchanged cases to obtain a better result.

For portable analyzer checks, run `python native/MatchedPredictionChecks.py`.
The analyzer writes `analysis.json` with create-new semantics. Existing analysis files cannot be overwritten.
See [the reference](../Reference/matched-prediction.md) for fields, formulas, and unresolved eligibility requirements.
