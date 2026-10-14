# Development practices

Use private domain representations and `Result` parsers for bounded settings.
Keep the timing seam internal. Test exact delay accounting with a controlled clock.
Use the real CPU workload for measurement validity. Timing noise must not weaken deterministic assertions.
Preserve raw observations and the seed. A seed reproduces intervention order, not operating-system scheduling.
Do not add test-output calls. Put failure details in assertions.
Run both required adversarial reviews before a code commit.
Keep the MiniScaffold FAKE target graph in `build/build.fs`. Make scripts and CI call its targets.
Update human-readable documentation in `docsSrc/` with each feature or bug fix.
Use `/divio-docs-system` to separate tutorials, how-to guides, reference, and explanation.
Keep navigation in [the documentation index](../docsSrc/index.md) aligned with those reader needs.
Describe implemented behavior separately from planned work and unverified platform support.
Run the repository's `BuildDocs` target to check documentation generation.

```powershell
dotnet test tests/CozNet.Tests/CozNet.Tests.fsproj -c Release
dotnet run --project samples/CozNet.Serial -c Release
```

```mermaid
flowchart LR
    Contract --> DeterministicTests --> RealWorkload --> Review
```

Update [the engine description](profiling/summary.md) when its behavior changes.
See [the build](build/summary.md) for packaging and documentation targets.
Use [the roadmap](plans/roadmap.md) to distinguish planned behavior from implemented behavior.
