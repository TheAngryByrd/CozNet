# Target-local support status

This page explains which parts of the target-local profiler you can use, what the evidence shows, and why none of it is qualified yet.

## Implemented is not qualified

CozNet uses two words with different meanings:
- **Implemented**: the code exists, and deterministic checks cover its rules.
- **Qualified**: a qualification package passes. Every suite then runs once on one clean tree, and each suite meets its fixed criteria.

`native/QualifyTargetLocal.ps1` runs the package. Its only package so far failed 6 of its 11 suites, and its tree was not clean. So the target-local profiler is experimental. Use its results as evidence to examine, not as a verified prediction.

## What you can use

A profiled run needs Linux x64 with a kernel that supports `PERF_FORMAT_LOST`. Tiered compilation and ReadyToRun must be off, and the perf map must be on. `CodeBinding` checks these settings.
The parsing of the perf map, the design checks, and the journal replay are managed code, and they run on any platform.

| Use | API | Suite result in the package |
| --- | --- | --- |
| Fork/join work with a final join | `TargetLocalProfiler.runBound` | `prediction` fails from capacity failures |
| Phases with a barrier | `TargetBarrier` | `barrier` fails from a capacity failure |
| A causal profile with an end-to-end or throughput point | `TargetCausalProfile.run`, `TargetProgressCounter.Visit` | `latency`, an end-to-end profile, and `throughput` pass |
| A bounded queue between two threads | `TargetQueue` | `pipeline` passes |
| A lock | `TargetLock` | `lock` fails from a prediction error, and `spinlock` fails from a capacity failure |
| A semaphore | `TargetSemaphore` | `semaphore` passes |
| Transaction latency with `Begin` and `End` | `TargetProgressCounter.Begin`, `End` | `transaction` and `spintransaction` fail from prediction errors |

A suite that passes in one package is not qualified. The earlier runs of every suite include failures.

## Two kinds of failure

- A **capacity failure** makes a block invalid. Examples are a debt above 2 ms, a profile with fewer than 100 visits, and a handler interval of at least 5 ms. A thread that the host keeps off the CPU cannot pay its debt, and the profiler reports this as a failure. It does not forgive the debt.
- A **prediction error** is a point outside its limit in valid blocks. The `selected-75` error of the transaction suite is the largest one, and its cause is not known.

## What you cannot use

- Windows, macOS, and Linux ARM64. The target-local path has no backend for them.
- Threads that do not register, the thread pool, tasks, and async code.
- Waits through `Monitor`, `System.Threading.Barrier`, a `SemaphoreSlim` that you call directly, I/O, or any blocking call outside the four target-local primitives.
- Automatic selection. You must name each selected, candidate, and eligible method as a perf-map symbol. [Candidate discovery](target-local-discovery.md) can rank the methods that a run sampled. Two live checks on Linux passed, one of the sample code and one of the library workflow, each in a workload with two methods. It ranks methods, not source lines, and it still needs a caller-supplied scope and eligible binding.
- Source lines, inlined callees, and samples in a callee of the selected method. Selection is by method range, with no callchain.

## Known limits of the evidence

- The profiler publishes each selected sample at once. Upstream Coz on Linux publishes when a thread releases another thread, or at a timer every 10 ms.
  The pinned source is Coz `256e3c389424d6a1691ee5d18b5ecb5a6c1001ec`: `libcoz_profiler.h:163-184`, `libcoz_libcoz.cpp:450-453` and `:555-558`, and `libcoz_profiler.cpp:615`. So a thread that waits after a sample can pay credit that upstream skips. The records cannot show how often this occurs.
- [The latency model](target-local-latency-model.md) is a deterministic model, not a CozNet execution. Its `release` policy is not upstream Coz, and it is not a proposed fix. A window in the model can include a publish that comes later in the same instant, so a single window is not a trace of source event order.
- The retained records do not bound payment overshoot. An earlier bound of 189 µs is withdrawn. An earlier claim that the place of the settlement keeps the total payment is withdrawn too.
- The boundary export gives ordered reads, not a snapshot at one instant. A few stop samples cannot give a stable latency estimate.

See [target-local prediction](target-local-prediction.md), [the API reference](../Reference/target-local-api.md), and [how to qualify the target-local profiler](../How_Tos/qualify-target-local.md).
