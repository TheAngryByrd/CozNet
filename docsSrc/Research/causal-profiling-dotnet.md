# Causal profiling for .NET: start fresh or use the existing fork?

Research date: 2026-10-13.

## Decision

**Start a new .NET implementation. Use Anthony Lloyd's project as a design reference and historical comparison.**

You already own [TheAngryByrd/Causal](https://github.com/TheAngryByrd/Causal). Its `master` matches upstream, with no commits ahead or behind. Creating another fork adds no value. [1]

Start with explicit regions for synchronous CPU work in controlled workloads. Treat automatic profiling of arbitrary .NET applications as a later project.

This recommendation rests on three findings:

1. The existing code demonstrates the idea, but lacks the experiment controls and runtime model needed for broad .NET use. [2][3][4]
2. The repository has no explicit license. GitHub permits public repository forks, but this does not supply general reuse rights. [1][5]
3. Most difficult work remains: delay accounting, synchronization, managed runtime effects, and evidence that predictions match real improvements. [3][6]

This is a source-based decision. No profiler, workload, build, or test ran during this research.

## What the article establishes

Lloyd describes two instrumented implementations. Both require calls to `regionStart` and `regionEnd` around code regions. [2]

The simple implementation adds proportional delays to regions. It compares delays applied everywhere with delays applied everywhere except the selected region. [2][4]

The full implementation tracks the union of time intervals when the selected region runs. Other regions receive delays at their end. The runner subtracts accumulated delay from elapsed time. [3]

Both implementations use busy loops. Lloyd reports similar results for one Fasta example. This supports a useful prototype, but does not establish general accuracy. [2][3][4]

The article proposes throughput and latency support as future work. The checked-in API measures repeated complete calls to `unit -> unit`. [2][3]

The underlying Coz technique has stronger published evidence. Its authors compared predicted and actual improvements in native applications, including `ferret` and `dedup`. [6, §4.3]

## Repository findings

The inspected upstream commit is [`5c89682a3be1ea0ac4324f6f2d1395a5417384f1`](https://github.com/AnthonyLloyd/Causal/tree/5c89682a3be1ea0ac4324f6f2d1395a5417384f1).
Its commit date is 2019-10-13. The live repository metadata also reports its last push in October 2019. [1]

| Area | Verified source finding | Effect on the decision |
| --- | --- | --- |
| Build | The F# library targets `netstandard2.1`. The C# example targets `netcoreapp3.0`. [7] | Retargeting alone does not address correctness. |
| Maintenance | The complete tree contains a small library and one example. It contains no test project or CI workflow. [1] | There is little validation infrastructure to preserve. |
| License | GitHub reports `license: null`. The complete tree has no license file. [1] | Obtain permission before copying code into a distributable product. |
| Experiments | Every iteration uses the same delay order. A single zero-delay case supplies the baseline. [3][4] | Time-dependent effects can align with treatments. |
| State | Module-level mutable variables hold the experiment and delay state. Cleanup does not use `finally`. [3][4] | Concurrent sessions and exceptional exits need a new lifecycle model. |
| Coverage | Only marked regions participate. The full implementation applies delays when a non-selected region ends. [3] | Long or unmarked execution requires explicit coverage rules. |
| Time | Regions use elapsed `Stopwatch` timestamps. The runner converts elapsed ticks to `int`. [3][4] | Wall time is not CPU execution time. Long measurements also need wider storage. |
| Results | The runner prints a table. It has no public request-latency or throughput progress API. [3][4] | A usable library needs structured observations and progress measures. |

The license finding does not mean GitHub forbids the existing fork. GitHub distinguishes platform fork rights from a license to reproduce or distribute software elsewhere. [5]

If the author grants a suitable license, a fork becomes viable for a narrow continuation of the demo. The runtime and validity work still remains.

## Why the runtime model matters

Coz does more than add arbitrary delays. Its paper specifies randomized experiments, effective experiment duration, and delay accounting across threads. [6, §§2–3]

Coz credits delays across synchronization. A thread that wakes because of another thread must not pay delays already imposed on that dependency. [6, §3.4.1]

Its native implementation intercepts blocking and waking operations. Lloyd's region API has no equivalent synchronization model. [3][6]

This difference does not prove that Lloyd's Fasta results are wrong. It means those results do not justify support for arbitrary managed concurrency.

An `await` can leave a region open while no CPU executes its code. Applying a delay proportional to that elapsed time would model waiting as work. [3]

**Do not put an elapsed-time region around arbitrary `await` operations and describe the result as a CPU speedup prediction.**

Keep request lifetime separate from the CPU regions selected for interventions. An async-aware design needs explicit rules for continuations, waits, cancellation, and work completion.

Lloyd argues for spinning to avoid encouraging context switches. Coz instead uses `nanosleep` and corrects excess sleep time. [2][6, §3.4]

Thus, spinning is not a universal requirement of causal profiling. Its effect on CPU contention and managed scheduling needs measurement.

GC, ThreadPool decisions, JIT changes, and external I/O do not automatically scale with marked regions. Their effect on prediction accuracy remains an experimental question.

## Statistics and experiment design

The MAD article explains robust dispersion and median-based outlier detection. The performance article develops repeated measurement and comparisons with Welch's test. [8][9]

These are useful references. They do not establish a confidence guarantee for the causal profiler's separate estimator. [3][10]

The profiler removes samples outside three MADs. It estimates error with a `1.253` multiplier and combines errors in differences and ratios. [10]

The source also contains a concrete median defect after trimming. It uses `i+j % 2` instead of `(i+j) % 2`. [10]

For sorted input `[-100, 1, 2, 3, 100]`, trimming leaves indices `1..3`. The code returns `2.5`, although their median is `2`.
This is a direct arithmetic trace of the source, not an executed test.

The error formula divides by `n * (n - 1)`. It therefore cannot produce a meaningful standard error for the first iteration's single observation. [10]

For a new implementation:

- Randomize treatment order within repeated blocks. Retain zero-intervention controls throughout the run.
- Keep raw observations, experiment order, actual delays, and runtime settings.
- Report retained and excluded sample counts. Keep GC and scheduling events visible.
- Define the estimate precisely: elapsed-time reduction, throughput increase, or average latency reduction.
- Separate uncertainty in measurements from systematic error in the intervention model.
- Validate interval coverage and small-sample behavior before presenting a confidence claim.

These are proposed requirements. The paper supports randomization and explicit baseline accounting, but it does not validate this proposed .NET design. [6]

For clarity, a 20% reduction in execution time implies a 25% throughput increase for fixed work. These percentages need different labels.

## What current .NET tools can supply

| Facility | Useful role | Important boundary |
| --- | --- | --- |
| EventPipe | Collect runtime events and performance observations. [11] | Its documented event pipeline does not supply selective causal delay control. |
| CLR profiling and ReJIT | Add instrumentation to managed methods. [12] | ReJIT affects future invocations and requires inlining and runtime coordination. |
| `SuspendRuntime` | Suspend managed runtime execution. [13] | Suspending the whole runtime does not implement a selective virtual speedup. |
| Stack snapshots | Associate execution with managed code. [14] | Asynchronous walks have documented deadlock and access-violation risks. |
| Native Coz | Reference algorithm, experiment format, and native comparison cases. [15] | Its README excludes current JIT-language support. It is not a ready .NET backend. |

Use EventPipe for diagnostics around experiments. Keep the intervention engine separate. Defer automatic instrumentation and Native AOT until the controlled model works.

The cited CLR API pages do not establish a complete current CoreCLR platform matrix. Verify each API on selected operating systems before committing to a native backend.

## Alternatives

| Approach | Assessment |
| --- | --- |
| Continue the existing fork | Fastest route to reproduce the old demo. Needs license clearance for broader reuse and substantial redesign for broader claims. |
| New managed library | Recommended starting point. Makes scope, lifecycle, measurements, and restrictions explicit. |
| Fork native Coz | Its BSD license permits reuse under stated conditions. Its native sampling and runtime assumptions still need major .NET integration. [15] |
| Build an automatic CLR profiler first | Appropriate only if automatic coverage is essential immediately. It combines profiling infrastructure risk with experiment-model risk. |
| Use ordinary profilers and benchmarks only | Useful for candidate selection and real-change measurement. They do not supply the causal intervention that answers this question. [6][11] |

Do not recreate existing telemetry or benchmark infrastructure without need. Expecto already exposes comparisons between real implementations. Those comparisons answer a different question. [16]

## Starting plan and proof criteria

These stages are proposed work. No stage has passed yet.

1. **Define the contract.** Support synchronous CPU regions in bounded, repeatable workloads. Choose current supported .NET and initial Windows/Linux targets.
2. **Build a small experimental core.** Use explicit sessions, 64-bit timestamps, structured results, randomized treatments, and separate progress counters.
3. **Prove simple predictions.** Compare interventions with real reductions in CPU work over several effect sizes. Verify unchanged functional output.
4. **Test concurrency limits.** Add critical-path changes, locks, producer/consumer queues, overlapping regions, and nested regions.
5. **Add runtime effects.** Measure GC, ThreadPool saturation, and tiered compilation. Add async support only after its accounting passes these checks.
6. **Consider automation.** Add CLR instrumentation or sampling only after the same proof suite passes with automatic coverage.

The first proof suite should contain:

| Case | Required evidence |
| --- | --- |
| Zero intervention | Estimates remain consistent with no effect. Instrumentation overhead is measured separately. |
| Serial known work | Predicted changes match actual reductions of selected CPU work. |
| Unequal parallel branches | Improving non-critical work has little effect. Improving critical work stops helping when another branch dominates. |
| Lock or queue dependency | Delays do not count the same dependency twice. Predictions match an actual changed implementation. |
| Waiting and external I/O | The tool rejects unsupported scopes or shows that its model remains accurate. |
| Nested and overlapping regions | The selected semantics prevent double counting and survive exceptional exits. |

Set acceptable prediction error before implementation. A reasonable initial proposal is two percentage points on stable synthetic cases, with measurement uncertainty reported separately.
Do not treat this proposed threshold as a measured capability. Require useful accuracy across independent runs and more than one machine before expanding claims.

## Sources and reading coverage

[1] GitHub primary metadata: [upstream](https://api.github.com/repos/AnthonyLloyd/Causal), [pinned commit](https://api.github.com/repos/AnthonyLloyd/Causal/commits/5c89682a3be1ea0ac4324f6f2d1395a5417384f1), [complete tree](https://api.github.com/repos/AnthonyLloyd/Causal/git/trees/5c89682a3be1ea0ac4324f6f2d1395a5417384f1?recursive=1), and [existing fork comparison](https://api.github.com/repos/AnthonyLloyd/Causal/compare/master...TheAngryByrd:master). Read live on the research date. The branch comparison can change.

[2] Anthony Lloyd, [Causal Profiling in .NET](https://anthonylloyd.github.io/blog/2019/10/11/causal-profiling), 2019. Read the complete article.

[3] [Pinned `Perf.fs`](https://github.com/AnthonyLloyd/Causal/blob/5c89682a3be1ea0ac4324f6f2d1395a5417384f1/dbg/Perf.fs). Read the complete implementation and Fasta caller.

[4] [Pinned `PerfSimple.fs`](https://github.com/AnthonyLloyd/Causal/blob/5c89682a3be1ea0ac4324f6f2d1395a5417384f1/dbg/PerfSimple.fs). Read the complete implementation.

[5] GitHub, [Licensing a repository](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository). Read the licensing and public-fork distinction.

[6] Charlie Curtsinger and Emery D. Berger, [Coz: Finding Code that Counts with Causal Profiling](https://arxiv.org/html/1608.03676v1), SOSP 2015. Read the overview, implementation, accuracy, and overhead sections. Consulted the evaluation and related-work text. The paper's entire bibliography was not independently retrieved.

[7] Pinned [library project](https://github.com/AnthonyLloyd/Causal/blob/5c89682a3be1ea0ac4324f6f2d1395a5417384f1/dbg/dbg.fsproj), [example project](https://github.com/AnthonyLloyd/Causal/blob/5c89682a3be1ea0ac4324f6f2d1395a5417384f1/run/run.csproj), and [README](https://github.com/AnthonyLloyd/Causal/blob/5c89682a3be1ea0ac4324f6f2d1395a5417384f1/README.md). Read in full.

[8] Anthony Lloyd, [Get MAD with Outliers with an Improved Median Function](https://anthonylloyd.github.io/blog/2016/10/21/MAD-Outliers). Read the article and code. Did not reproduce its benchmark results.

[9] Anthony Lloyd, [Modularity from Lazy Evaluation - Performance Testing](https://anthonylloyd.github.io/blog/2016/05/20/performance-testing). Read the article and code. Its confidence wording is an author claim, not independently verified coverage.

[10] [Pinned `Statistics.fs`](https://github.com/AnthonyLloyd/Causal/blob/5c89682a3be1ea0ac4324f6f2d1395a5417384f1/dbg/Statistics.fs). Read in full with `ListSlim.fs`.

[11] Microsoft, [EventPipe](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventpipe).

[12] Microsoft, [`ICorProfilerInfo4::RequestReJIT`](https://learn.microsoft.com/en-us/dotnet/framework/unmanaged-api/profiling/icorprofilerinfo4-requestrejit-method).

[13] Microsoft, [`ICorProfilerInfo10::SuspendRuntime`](https://learn.microsoft.com/en-us/dotnet/framework/unmanaged-api/profiling/icorprofilerinfo10-suspendruntime-method).

[14] Microsoft, [`ICorProfilerInfo2::DoStackSnapshot`](https://learn.microsoft.com/en-us/dotnet/framework/unmanaged-api/profiling/icorprofilerinfo2-dostacksnapshot-method).

[15] Coz at [`256e3c389424d6a1691ee5d18b5ecb5a6c1001ec`](https://github.com/plasma-umass/coz/tree/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec): read [README](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/README.md), [license](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/LICENSE.md), thread-state source, and selected experiment/delay code. No full source audit or platform validation occurred.

[16] Expecto at [`dce19d6d9e83464bfab098c83af0cda76af737e9`](https://github.com/haf/expecto/tree/dce19d6d9e83464bfab098c83af0cda76af737e9): inspected `isFasterThan` and measurement/comparison code in `Expect.fs` and `Performance.fs`.

[17] Anthony Lloyd, [Kicking the Debugger habit](https://anthonylloyd.github.io/blog/2017/04/30/kicking-the-debugger). Read the article's argument for explicit diagnostic instrumentation. This explains the API style, not causal accuracy.

[18] Embedded [Emery Berger talk](https://www.youtube.com/watch?v=r-TLSBdHe1A). Opened the page but did not watch the video. No usable English transcript was obtained. Used the authors' paper for technical claims.

Coverage includes every substantive direct reference in Lloyd's article. It does not include every recursive citation, linked project dependency, or unrelated navigation link.
