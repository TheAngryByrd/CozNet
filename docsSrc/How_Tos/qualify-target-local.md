# How to run every target-local suite as one package

Use this procedure to run every target-local suite on one tree and collect one record of the results.
It requires the .NET 10 SDK, PowerShell 7, Python 3, Docker, and the images `coznet-native-build:10.0.100` and `mcr.microsoft.com/dotnet/sdk:10.0.100`.
It does not provision a host. The Docker engine must run Linux x64 containers.

1. Commit or remove all changes. A changed file does not stop the run, but the package then records `treeClean` as false.
2. Make sure that no other package or target-local runner runs. The runners share the folders under `artifacts`, so a second run can put a wrong folder in the package.
3. Run this command from the repository root:

   ```sh
   dotnet run --project build/build.fsproj -- -t TargetLocalQualify
   ```

   The command runs the eleven suites one after the other. A failed suite does not stop the later suites.
4. Open the new directory under `artifacts/target-local-qualification`.
5. Open `package-summary.json`.
   - `passed` is true only when every suite passed and its runner exited with 0.
   - `passed` also needs unchanged inputs, source hashes equal to the start hashes, and no file changed during the package.
   - `suites` lists the reasons for each suite that failed.
   - `treeClean` is true only when `git status --porcelain` was empty at the start.
6. Open `package.json` to find the commit, the start hashes, and the run directory of each suite. Each run directory holds the evidence of its suite.

A package that does not pass makes the command fail. Do not run the package again to get a pass. Keep each package and each run as evidence.
To claim a qualification, cite a package with `passed` and `treeClean` both true.
