# CozNet

CozNet is an experimental .NET causal profiling library. It uses F# and targets .NET 10.
The initial engine supports serial, synchronous CPU regions only.
The target-local profiler passes builds and runtime checks on Linux x64 only. It supports CPU-bound fork/join threads that wait only at a `TargetBarrier` before the join. Barrier qualification runs pass and fail on this host. The failures correlate with run-queue delay and possible SMT contention. One failure predates the diagnostics. In the latest set of runs, the barrier, forced-GC, latency profile, prediction, throughput, pipeline, and semaphore suites pass. The lock suite fails five points and the spin lock suite four invalid blocks. The latest transaction run passes five of six points with five valid blocks, and only `selected-75` fails. The two runs before it each have one block invalid from a debt failure after a wake. The spin transaction suite passes five points. Pinning the workers to different physical cores does not shorten the plain handoff of the `transaction` suite. A matched warm-up and collection in the plain arms removes the plain handoff gap. In one run, a payment before each progress stamp removes most of the `selected` bias at 25% and 50%. It corrects too much at 75% in three runs, and a sample period of 0.25 ms reduces but does not halve that error. In one run, the long queue handoffs do not have the producer and the consumer on one CPU at their end points, and their cause is open. The lock suite still fails points in valid blocks. A ReJIT check shows that a ReJIT of a bound method fails the run.
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
