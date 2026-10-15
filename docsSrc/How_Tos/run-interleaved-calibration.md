# How to run interleaved CPU calibration

Use this procedure to retain one declared series with short baseline, optimized, and control segments.
Install the repository SDK and PowerShell requirements. Docker must run Linux containers with local image `mcr.microsoft.com/dotnet/sdk:10.0.100`.

1. Run the build target from the repository root.

   ```powershell
   ./build.cmd LinuxInterleavedCalibration
   ```

   On Linux, use `./build.sh LinuxInterleavedCalibration`.

2. Open the printed directory under `artifacts/interleaved-calibration/`.
   Inspect `summary.json` and `outcomes.json` for failed processes and missing records.

3. Read `calibration.json.plan.json` for the saved order.
   Read `calibration.json` for blocks, failures, and statistics.
   If execution stopped, inspect complete rows in `calibration.json.blocks.jsonl` for earlier blocks.

4. Retain the whole directory, including failures. Do not repeat an unchanged series to obtain a better interval.

The runner captures source and binary snapshots before execution and checks their hashes afterward.
The source snapshot is not a complete rebuild checkout.
The runner checks portable contracts on the host and Linux, then runs physical baseline, optimized, and calibration cases on Linux.
Each Linux process has a 30-second watchdog.
Completion does not establish prediction accuracy.

See [the record reference](../Reference/interleaved-calibration.md) and [the interpretation limits](../Explanations/calibration-sensitivity.md).
The [contiguous procedure](run-physical-calibration.md) remains available as a separate protocol.
