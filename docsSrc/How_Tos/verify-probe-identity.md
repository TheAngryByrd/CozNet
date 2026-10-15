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
Each record must retain a nonzero `nativeRange` captured before invalidation.
The cases check exact range boundaries and rejection of each foreign binding field.
Unload records must show collection of the target context and lifetime state 7.
Duplicate-module records must show lifetime state 5.
Both invalidation cases must reject later binding, site, native range, and copied-map queries.
The summary must report successful cases and unchanged inputs.

The native `probe-lifetime` check covers state transitions, range publication, and publication racing with invalidation.
It also checks that competing publishers cannot replace the successful publisher's range.
The runner also checks managed range parsing, capture failure, and permanent rejection during resolution.
It retains the existing native service and managed accounting checks.
Retain failed records. A passing lifetime check does not establish payment integration or prediction accuracy.
The boundary checks use synthetic addresses. They do not prove that a real CPU sample belongs to the method.
The [guard-removal result](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/mutations/bound-map-publication.md) records the concurrency check's observed defect sensitivity.

See [the reference](../Reference/probe-identity.md) for state values and compilation limits.
