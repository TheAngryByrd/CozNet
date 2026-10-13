# Managed runtime portability evidence

The 43 managed tests pass on Windows x64 and Linux x64.
The checks include live cooperative waiting, cancellation recovery, thread ownership, concurrent sample updates, and sample-credit arithmetic.
They do not establish Linux support for the native profiler.

Source tree: `f2a4e448775cacf4c6f66341003177657776860e`.
The subsequent edits for this record change documentation only.
Windows used SDK 10.0.112 and runtime 10.0.12 in Release.
Linux used SDK 10.0.100 and runtime 10.0.0 on Ubuntu 24.04 x64 in Release.
The Linux container image was `mcr.microsoft.com/dotnet/sdk:10.0.100`.
Its local image ID was `sha256:3db72bd242b5f26a45d74c2d49a7654740e75c732654658177e52aa498645c14`.

The Linux run extracted a Git archive of the staged tree into the container's temporary filesystem.
The input mount was read-only. The container was removed after exit.
SourceLink and source-control queries were disabled because the archive contains no `.git` directory.
The runtime code and behavioral assertions were unchanged.

```sh
dotnet test tests/CozNet.Tests/CozNet.Tests.fsproj -c Release -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false
```

Linux result: `Failed: 0, Passed: 43, Skipped: 0, Total: 43`, exit code 0.
Windows also passed the same 43 tests and the Release `DotnetPack` graph.
Worker tests use five-second completion limits. The lock-release test uses a two-second update limit.
The inputs are deterministic. The live wait has a ten-millisecond minimum with no exact scheduler-timing assertion.

See [runtime contracts](../../../lode/profiling/cooperative-runtime.md) and [native evidence](../runtime-probes/README.md).
