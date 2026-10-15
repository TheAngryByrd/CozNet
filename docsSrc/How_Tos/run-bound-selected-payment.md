# How to run bound selected-owner payment

Install the repository SDK and prepare the Linux compiler and SDK container images.
Install Python 3 on the host for evidence replay.

1. Run the diagnostic:

   ```powershell
   dotnet run --project build/build.fsproj -- -t LinuxBoundSelectedPayment
   ```

2. Open `summary.json`, `analysis.json`, and `manifest.json` in the reported `artifacts/bound-selected-payment/` directory.
3. Inspect `0.json`, `50.json`, and `invalidate-after-selected-payment.json`.
4. For a failed case, inspect both `failureBoundary` and `selectedFailureBoundary` before interpreting native cleanup.
5. To repeat analysis without another timing run, use the retained analyzer:

   ```powershell
   python <run>/source/native/AnalyzeBoundSelectedPayment.py <run>
   ```

The target builds the Linux profiler, workload, and cooperative library.
It runs native checks, three managed groups, and both Python analysis suites.
It retains all three runtime cases before reporting failure.
Its analyzer runs from the retained source snapshot.

The zero-percent case must complete without payment.
The 50-percent case must settle both peer and selected leases within the existing bounds.
The invalidation case must retain selected payment, prevent resume, and report `rejectionVerified=true` with `completed=false`.

Keep failed runs. Do not repeat an unchanged capacity failure solely to obtain a pass.
The live containers disable Docker's seccomp filter. They retain a read-only root filesystem and have no network or capabilities.

See [the reference](../Reference/bound-sampled-payment.md) for the contracts and limits.
Use [the peer-only procedure](run-bound-sampled-payment.md) for the legacy command.


## Capture child CPU observations

After the target builds the required binaries, run the optional diagnostic:

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 -SelectedPayment -ChildCpu
```

Add `-PauseTiming` to retain collector pause phases in the same capture.
Inspect `Cpu` in retired raw receipts, failure captures, and cleanup copies.
Check each reading's status before interpreting its value.
Retain CPU read errors and inconsistent clocks with the original service failure.
Use the [CPU API reference](../Reference/cooperative-interop.md#optional-child-cpu-api) to interpret the records and limits.
