# How to verify CLR-bound CPU samples

Install the repository SDK and native build prerequisites.
Prepare the existing Linux compiler and SDK container images.

1. Run the portable checks on the host:

   ```powershell
   dotnet run --project build/build.fsproj -- -t BoundSamplesManagedVerify
   ```

2. Run the Linux x64 collection checks:

   ```powershell
   dotnet run --project build/build.fsproj -- -t LinuxBoundSamplesVerify
   ```

3. Inspect `summary.json`, `manifest.json`, and the four case records in the reported directory.

The Linux target builds the workload and profiler first.
It runs the portable checks in the container before the live cases.
The live containers use `seccomp=unconfined`, which disables Docker's seccomp filter for those containers.
They retain no capabilities or network access and have a read-only root filesystem.
This setting does not change the host's syscall policy.

The `collect` record must contain real selected-method samples and successful completion.
Runtime perf-map output must remain disabled in the manifest.
The final source journal must contain one enable and one disable transition.

The invalidation cases load a second matching module.
`before-enable` must reject admission without enabling the event or accepting a sample.
`during` must retain an earlier selected-method sample and reject completion.
`after-close` must retain earlier samples but reject completion after source closure.
All cases must retain collector-join and source-disposal evidence.

The summary must report four successful case checks, successful portable checks, and unchanged inputs.
Retain failed runs. These checks do not establish sampled payment capacity or prediction accuracy.

See [the collection reference](../Reference/bound-samples.md) for ownership and rejection rules.
