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

3. Inspect `summary.json`, `manifest.json`, and the five case records in the reported directory.

The Linux target builds the workload and profiler first.
The host target runs the one-shot and pausable managed groups in sequence.
It runs the portable checks in the container before the live cases.
The live containers use `seccomp=unconfined`, which disables Docker's seccomp filter for those containers.
They retain no capabilities or network access and have a read-only root filesystem.
This setting does not change the host's syscall policy.

The `collect` record must contain real selected-method samples and successful completion.
Its `provisionalSamples` count must be positive. The workload verifies that live reads match the completed collection prefix.
Runtime perf-map output must remain disabled in the manifest.
The final source journal must contain one enable and one disable transition.

The `pause` record must contain two completed pauses and three source epochs.
Check its `epochSamples` for retained samples from each eligibility epoch.
Check `excludedCounters` for two pairs of disabled readings with unchanged CPU counters.
The case must also reject a stop request from a foreign OS thread before it enables the source.
The workload performs owner CPU work between each pair.
The final batch must reconcile all retained samples across the pauses.

The invalidation cases load a second matching module.
`before-enable` must reject admission without enabling the event or accepting a sample.
`during` must retain an earlier selected-method sample and reject completion.
`after-close` must retain earlier samples but reject completion after source closure.
All cases must retain collector-join and source-disposal evidence.

The summary must report five successful case checks, successful portable checks, and unchanged inputs.
Both `managedExit` and `pausableManagedExit` must be zero.
Retain failed runs. These checks do not establish sampled payment capacity or prediction accuracy.

See [the collection reference](../Reference/bound-samples.md) for ownership and rejection rules.
See [retained pause evidence](../../reliability/features/bound-samples/pausable-collection.md) for the controlled Linux result and its limits.
