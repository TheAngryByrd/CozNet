# Target-local qualification package

## Finding

Each target-local suite has its own runner and build target. No command runs every suite on one tree and collects one record of the results.
- The pages under `reliability/features/target-local` cite runs from different trees. For example, the latest `transaction` run uses the tree of the wake after idle plan, and the latest prediction run uses the tree of worker pinning.
- So no record shows the result of every suite on one tree.
- The user decision of 2026-10-19 asks for a reproducible qualification command and evidence package that does not depend on one host, if it is missing. It must not provision infrastructure.

## Decision

`native/QualifyTargetLocal.ps1` runs every target-local suite once, in a fixed order, and writes one package.
- The suites are `prediction`, `transitions`, `barrier`, `latency`, `throughput`, `pipeline`, `lock`, `spinlock`, `semaphore`, `transaction`, and `spintransaction`. Each runs with the default arguments of its build target, one after the other.
- Before the first suite, the package records the commit and the output of `git status --porcelain`. It also records the SHA-256, after CRLF to LF, of each file under `native`, `src`, `samples`, `lode/plans`, and `build`, and at the repository root, that is tracked or not ignored.
  These files hold every source that a runner hashes and the shared build files. The package does not stop for changes outside the commit.
- After the last suite, the package lists and hashes these files again. A file that changed, was added, or was removed during the package fails it.
- For each suite, the package records the runner, its arguments, its exit code, and the one new run folder. It passes its Python command to each runner.
- A summary or manifest that cannot be read is recorded as a read error, and the later suites still run. It reads `passed` and `inputsUnchanged` from the summary of that run, and the source hashes from its manifest, or from its summary when there is no manifest.
- `native/TargetLocalPackage.py` decides the verdict and writes `package-summary.json`. The package passes only when each runner exits with 0, and each suite has exactly one new run folder with a readable summary.
  Also, `passed` and `inputsUnchanged` must be true, each source hash must equal the start hash, and no file can change during the package.
- The package records `treeClean`, which is true only when `git status --porcelain` is empty. A changed file outside the commit does not change the verdict, but a claim of qualification must cite a package with `treeClean` true.
  On the host of this work, the unrelated changes in `src/Directory.Build.targets` and `tests/CozNet.LinuxPerf/BoundSampleCollection.cs` keep `treeClean` false.
- The runners write to the shared folders under `artifacts`, and the package finds the new folder of each suite. So only one package or runner can run at a time.
- A cancelled package writes no `package.json`, and its finished runs stay in their folders.
- The command needs Docker, PowerShell 7, Python 3, the .NET SDK, and the two container images that the runners use. It runs on any host that has them. It does not provision anything.
- The build target `TargetLocalQualify` runs it. The package goes to `artifacts/target-local-qualification/<run>`, and the command exits with 1 when the package does not pass. A failed suite does not stop the later suites.
- The runners, the analyzers, the criteria, and the bounds do not change.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 126 and change none.

127. Unit checks of `native/TargetLocalPackage.py` show that a package fails when a runner exits with a code other than 0, or when a suite fails, has a read error, or has no summary.
     They also show that it fails when a suite has no run folder or more than one, has changed inputs, or has a source hash other than its start hash or no start hash, and when a file changed during the package. They also show that a package of passed suites passes, and that `treeClean` follows the status.
128. On the reviewed tree, the package runs once. The package and each of its runs are retained, whatever the verdict.
129. After the commit, every source hash in the package equals the SHA-256 of the committed blob, and the package records that commit or its parent.

See [target-local status](target-local-status.md) and [the roadmap](roadmap.md).
