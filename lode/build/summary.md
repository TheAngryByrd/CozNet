# MiniScaffold build

`build/build.fs` owns the FAKE target graph. Its baseline is MiniScaffold's `v0.40.0-beta001` library template.
The [release package](https://github.com/TheAngryByrd/MiniScaffold/releases/tag/v0.40.0-beta001) matches commit `bb58c5acb91c6bc34648ec8829d7866ffaa049e2` for these build files.
The release still contains `SourceLinkTest`. CozNet removes that target and its CLI helper at the user's request.
`build/Changelog.fs` supplies release-note handling. `build/FsDocs.fs` supplies documentation commands.
The build project targets .NET 10 and has a committed package lock.
`build.cmd` and `build.sh` restore local tools and forward target arguments.
CI invokes the same `DotnetPack` target with `CONFIGURATION=Release`.

```powershell
./build.cmd DotnetPack
./build.cmd BuildDocs
./build.cmd GenerateCoverageReport
dotnet run --project build/build.fsproj -- --list
```

`DotnetPack` is the default target. It cleans project outputs, restores, builds, tests, and packages the library into `dist/`.
On CI, the graph also checks formatting before compilation.
Cleanup resolves paths and rejects directories outside the repository before removal.
`BuildDocs` builds the solution and generates documentation under `docs/`.
Generated packages and documentation remain ignored.
AltCover supplies coverage collection. `ENABLE_COVERAGE=true` enables the template's 80% sequence-coverage threshold during tests.
`GenerateCoverageReport` collects coverage without that threshold and writes HTML under `docs/coverage/`.

The repository owner is `TheAngryByrd`. The release branch is `master`.
Release and publish targets remain explicit actions. Default packaging does not publish packages or create Git tags.
Keep build orchestration in this graph instead of duplicating it in scripts or CI.

```mermaid
flowchart LR
    Scripts --> FAKE
    CI --> FAKE
    FAKE --> Clean --> Restore --> Build --> Test --> Pack
    Restore --> FormatCheck[CI format check] --> Build
    Build --> Docs
```

See [practices](../practices.md), [the index](../lode-map.md), and [the README](../../README.md).
