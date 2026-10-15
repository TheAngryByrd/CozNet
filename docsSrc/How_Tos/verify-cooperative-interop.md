# How to verify cooperative interop

Use these checks to verify the internal native boundary, managed receipt tracking, and ledger settlement.
They do not run a sampled profiling experiment.

1. Install the repository's .NET SDK and a C++20 compiler with CMake.
2. Run the host checks:

   ```powershell
   dotnet run --project build/build.fsproj -- -t CooperativeInteropVerify
   ```

3. For Linux container checks, use a Linux Docker engine with the existing compiler and SDK images.
   The required images are `coznet-native-build:10.0.100` and `mcr.microsoft.com/dotnet/sdk:10.0.100`.

   ```powershell
   dotnet run --project build/build.fsproj -- -t LinuxCooperativeInteropVerify
   ```

4. Open the reported directory under `artifacts/cooperative-interop/`.
   Inspect `summary.json`, `manifest.json`, and the six case records.

The summary must report unchanged inputs, a successful managed contract check, and six successful case checks.
The `late` and `clock-failure` cases succeed only when receipt validation rejects the evidence permanently.
Their ledger reservations must remain pending even after native cleanup succeeds.
The `active-cleanup` case must cancel and poll an unclaimed request before release, retirement, and join.
The other cases must verify a new independent generation without repairing any previous failed ledger.
Retain failed cases. Do not repeat unchanged timing cases to obtain a pass.

For pure managed checks only, run the `CooperativeManagedVerify` build target.
See [the reference](../Reference/cooperative-managed.md) for ownership, timing, and support limits.
