# How to verify injected probe identity

Use these checks to verify the internal startup binding and its unload notification.
Install the repository SDK and the native build prerequisites first.

1. For Windows x64, run the build target:

   ```powershell
   dotnet run --project build/build.fsproj -- -t ProbeIdentityVerify
   ```

2. For Linux x64 containers, use the existing compiler and SDK images:

   ```powershell
   dotnet run --project build/build.fsproj -- -t LinuxProbeIdentityVerify
   ```

3. Inspect `summary.json`, `manifest.json`, and the twelve case records in the reported directory.

Each target builds the profiler and managed workload before verification.

The cases cover identity, collectible-module unload, and a second matching module at four probe intervals.
Identity records must retain the injected generation, exact entry/loop counts, and the selected method token.
Unload records must show collection of the target context and lifetime state 7.
Duplicate-module records must show lifetime state 5.
Both invalidation cases must reject later binding and site queries.
The summary must report successful cases and unchanged inputs.

The native `probe-lifetime` check covers state transitions and publication racing with invalidation.
The runner also executes the existing native service checks and pure managed accounting checks.
Retain failed records. A passing lifetime check does not establish payment integration or prediction accuracy.

See [the reference](../Reference/probe-identity.md) for state values and compilation limits.
