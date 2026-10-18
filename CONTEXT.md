# CozNet

CozNet is an experimental .NET causal profiling library. It uses F# and targets .NET 10.
The initial engine supports serial, synchronous CPU regions only.
The target-local profiler passes builds and runtime checks on Linux x64 only. It supports CPU-bound fork/join threads that wait only at a `TargetBarrier` before the join. Barrier qualification runs pass and fail on this host. The failures correlate with run-queue delay and possible SMT contention. One failure predates the diagnostics. The latest prediction and barrier runs both fail.
See `docsSrc/Tutorials/Target_Local_Prediction.md` and `docsSrc/Explanations/target-local-prediction.md`.
Read `terminology.md` and `docsSrc/Explanations/Background.md` before changing the experiment model.

The repository started from MiniScaffold. Use `master` as the default branch.
Use Expecto for tests. Keep raw measurements and fixed seeds for experiment inspection.
Do not claim support for parallel work, asynchronous work, blocking I/O, or automatic instrumentation.
The one exception is the qualified target-local fork/join and `TargetBarrier` model above. Do not extend it to other parallel work or other waits.
Do not copy code from AnthonyLloyd/Causal without a suitable license.
Keep shared code portable. Isolate platform APIs behind explicit backend interfaces.
Report platform support only after builds and runtime checks pass on that platform.

Run `dotnet test tests/CozNet.Tests/CozNet.Tests.fsproj -c Release` for deterministic tests.
Run the serial sample separately. Timing measurements are not deterministic unit tests.

Before committing, follow the user's `pre-commit-adversarial-review` skill.
