# How to run sample-driven bound peer payment

Install the repository SDK and prepare the existing Linux compiler and SDK container images.
Install Python 3 on the host for evidence replay and its portable checks.

1. Run the diagnostic:

   ```powershell
   dotnet run --project build/build.fsproj -- -t LinuxBoundSampledPayment
   ```

2. Open `summary.json`, `analysis.json`, and `manifest.json` in the reported `artifacts/bound-sampled-payment/` directory.
3. Inspect `0.json`, `50.json`, and `invalidate-after-payment.json`.

The target builds the profiler, workload, and cooperative library.
It runs native contracts and both portable managed contract groups before the live cases.
It also runs portable Python analysis checks and replays the retained live evidence.
The cases run in the declared order: zero percent, 50 percent, then invalidation after payment.
The runner retains every case before it reports failure.

The zero-percent case must complete without credit or payment.
The 50-percent case must retain selected samples, positive credit, accepted peer payments, and debt within the existing limits.
The invalidation case must retain earlier real payment and report `rejectionVerified=true` with `completed=false`.
Inspect cleanup results and failures in each record. Do not treat the summary alone as proof of prediction accuracy.

The live containers use `seccomp=unconfined`, which disables Docker's seccomp filter for those containers.
They have no capabilities or network access and use a read-only root filesystem.
Runtime perf-map output remains disabled.

Retain failed runs. Do not rerun an unchanged capacity failure solely to obtain a pass.
See [the reference](../Reference/bound-sampled-payment.md) for ownership, accounting, and verification limits.
