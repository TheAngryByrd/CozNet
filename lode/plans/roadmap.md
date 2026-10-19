# Capability roadmap

The implemented scope is a serial experiment prototype. The goal is a .NET implementation of sampled Coz semantics.
Keep the serial model separate from the proposed sampled engine.

The [target-local sample plan](target-local-samples.md) has a portable decoder, a Linux ring adapter, and [bounded dispatch](target-local-dispatch.md).
The [native ledger](target-local-credit.md) credits and pays target-locally in a native two-thread run.
The [CLR fork/join qualification](target-local-prediction.md) passes its fixed criteria with managed replay in most runs. The latest six runs pass. The run before them fails by one startup debt failure and one block of -12.57 points, the three before that pass, and the run before them fails `peer-longer` by one invalid block from a startup debt failure. [The causal profile](target-local-causal-profile.md) checks six points of a profile against real optimizations. Its first run, four later runs, and the latest seven pass. The three before those and three earlier runs fail one or two points, or one block from a debt failure. [Throughput progress points](target-local-throughput.md) pass all six points in their first five runs. The sixth fails by one invalid block with a handler interval above 5 ms, the seventh to twelfth pass, the thirteenth fails one point by one block, the fourteenth to sixteenth pass, and the seventeenth fails one point by one block. [The blocking queue](target-local-queue.md) passes five of six pipeline points in each of its first three runs, and all six in its fourth and fifth, with the [rotated order](target-local-rotation.md). Its sixth fails `peer-50` by one block, its seventh passes, its eighth fails `peer-25` by one block, its ninth passes, and its tenth fails by a debt failure after a wake with a token-limited skip and by one `peer-50` block. Its eleventh and twelfth pass, its thirteenth and fourteenth fail `peer` points, and its fifteenth passes, and its sixteenth fails two points by one block each. [The lock](target-local-lock.md) fails its first run at the knee of its critical section. [Handoff counts](target-local-lock-handoff.md) show a higher handoff share in the profile than in the real optimization at the knee. [A 1 ms spin](target-local-spin-lock.md) before a lock wait lowers that share and the knee error, but its first run has two blocks with too few visits. [`TargetSemaphore`](target-local-semaphore.md) carries the wake rule to threads that signal each other, and its first run passes four of six points, its second passes all six, its third fails two points, its fourth fails five points, four by one block each and `peer-50` by two blocks, its fifth and sixth each fail two points, its seventh fails one, and its eighth and ninth pass. [The lock](target-local-lock.md) passes all six points in its seventh run and fails points in its eighth to twelfth. [Latency progress points](target-local-transactions.md) fail all six points in their first run. [A per-transaction rule](target-local-transaction-delay.md) passes four of six points in the second and third runs. In the third run, the mean [queue handoff](target-local-transaction-handoff.md) of the plain original arm is about 150 microseconds longer than that of the profile baseline. [A consumer spin](target-local-spin-queue.md) leaves a gap of about 98 microseconds, so the spin did not close the gap. [Handoff parts](target-local-handoff-parts.md) show that the plain enqueue alone is 32 microseconds longer, which the records do not place, and that a debt failure follows one child payment of 3.35 ms for a request of 50 microseconds. [Failure records](target-local-failure-records.md) refute GC suspension for the latest latency debt failure. [Failure context](target-local-failure-context.md) shows that the latest debt failures follow a late start, or a wake whose release token limits its skip. [Experiment diagnostics](target-local-experiment-diagnostics.md) show that the `peer` 50% prediction tracks the producer time outside measured payments. The link is descriptive. [A warm-up](target-local-warmup.md) experiment did not remove the order-0 producer ratio. [The rotated order](target-local-rotation.md) now moves each point through five positions.
The [forced-GC check](target-local-transitions.md) passes 10 of 10 in thirty checks, including the latest, and fails 1 of 10 by the 2 ms debt bound in nine current-design checks. In the latest failure, two [GC windows](target-local-gc-windows.md) cover 0.84 ms of the 6.51 ms failure interval. [The ReJIT check](target-local-rejit.md) shows that a ReJIT of a bound method fails the run.
The [release-token wake rule](target-local-blocking.md) works for `TargetBarrier`. Twenty-seven of the thirty-eight barrier qualification runs pass, including the latest five. The one before them fails `barrier-peer-bound` by its median of 5.10 points. [The start setup](target-local-start-setup.md) moves about 0.7 ms of profiler setup out of the sampled interval.
[Code binding and shutdown](target-local-lifecycle.md) are enforced and checked. [Per-wait run-queue delay](target-local-wait-delay.md) now places a debt failure after a wake. Run-queue delay, possible SMT contention, and variation between arms correlate with the failures.
[Worker pinning](target-local-worker-pinning.md) to different physical cores does not shorten the plain handoff of the `transaction` suite, and it does not raise the total of the points that pass.
The user decided on 2026-10-19 to keep the 2 ms debt bound, measured wall-time payment, the release-token rule, and all criteria. Failed runs stay failed, WSL2 stays unqualified where its evidence fails, and a bare-metal qualification is deferred. See [the plain warm-up plan](target-local-plain-warmup.md).
Two problems are separate. Capacity failures are invalid blocks from a debt failure or too few visits. Prediction errors are point failures in valid blocks without a capacity failure, and their cause is not shown to be the host.
[A matched warm-up of the plain arms](target-local-plain-warmup.md) removes the plain handoff gap in both transaction suites. The prediction errors of the transaction and lock suites stay, in valid blocks.
The remaining `transaction` errors have two parts: a `selected` bias in every run, and one slow baseline that moves all points of its block.
[A payment before each progress stamp](target-local-progress-settlement.md) removes most of the `selected` bias at 25% and 50%, and the `spintransaction` suite passes five points. At 75% the change corrects too much, by about 12 points in one `transaction` run.
[A sample period of 0.25 ms](target-local-sample-period.md) reduces the `selected-75` error from -10.92 to -6.42 points, but it does not halve it, and it adds handler cost. The cause of the error at 75% stays open.
[The CPU placement of each handoff](target-local-handoff-placement.md) rejects a shared CPU at the end points of the long handoffs in one run. A long wake followed by a move stays possible, and the cause of the long handoffs stays open.
The latest `transaction` run passes five points with five valid blocks. `selected-75` fails in all three runs at 1 ms after the settlement change, by about -11 points, and two of the five runs after that change have a debt failure.
Next, find why `selected-75` predicts too little gain after the settlement change.
The release-token wake rule is a design choice of [the blocking plan](target-local-blocking.md), and it stays. The 2 ms debt bound is a user requirement of the port goal, and the repository does not otherwise record it as one.
The two debt failures with [failure context](target-local-status.md#failure-context) follow a late start or a wake, and neither has an overshooting payment. The pipeline failure also has no collection.
Two earlier debt failures, in latency run `20261019T090901082-03de2f25` and lock run `20261019T090501903-273c483d`, have no context records and stay unexplained. The latency failure is on the selected thread, 25.0 ms after the first timed journal event, with no wake. The [cpuset diagnostic](target-local-status.md#open-failures) with one CPU for each physical core also records a debt failure after a delayed wake.
The managed drain dependency remains in the live backend exposed by [nominal timer diagnostics](timer-nominal-credit.md).
The timer path avoids loop probes. Its default interval policy still discards first-event weights.
The new callback-reuse comparison completes but retains about ten percentage points of shortfall.
This directs the next backend comparison; it does not establish a fix for capacity or prediction.

The [clock-backed matched block](../../reliability/features/native-signal/matched-sample-membership.md) checks retained source membership without changing recorded credit.
All treatment credit is source-inside, but sampled reduction remains 12.73–12.76 percentage points below physical reduction.
Next, investigate recurring execution cost and inline instrumentation attribution at fixed probe density.
The 324.9 ms treatment CPU increase does not identify a component cost. Preserve pending ownership and frozen ledger residuals.

The [selected CPU comparison](../../reliability/features/native-signal/selected-cpu.md) completes with valid CPU evidence in all four arms.
The sampled treatment consumes about 55.1 ms more selected CPU than its zero arm, with little CPU/elapsed separation inside the observed brackets.
Its prediction remains about 30 percentage points below physical reduction.
Next, isolate recurring probe, drain, and service CPU. Do not infer component savings from this single block alone.

The [prepared ownership service comparison](../../reliability/features/native-signal/prepared-service-ownership.md) passes twelve expected outcomes with matching inputs.
Owner/controller receipt and submission paths take less recorded time, both in total and per call.
The [prepared owner physical block](../../reliability/features/native-signal/prepared-owner-physical-comparison.md) still underpredicts by about 28.8 percentage points.
It freezes no pending native request, but retains a 65 ns selected ledger residual and two source-tail records.
Next, investigate sampled attribution and boundary membership. Do not treat more controller tuning as proof of prediction accuracy.


The [matched ownership comparison](../../reliability/features/native-signal/owner-controller-comparison.md) supports retaining owner/controller mode as an opt-in candidate.
Its shorter selected intervals do not qualify an overall speedup. Zero-speedup elapsed times vary substantially.
Measurement preparation now separates first-call capture work from the actual start. Its cost is observed, not bounded.
The prepared four-arm comparison still differs by about 42.6 percentage points between descriptive prediction and physical change.
The earlier arithmetic calibration uses unchanged method source but does not qualify the current instrumented program.
Next, investigate profiler overhead and sampled attribution. Repeating boundary preparation cannot resolve that mismatch.
The [idle-service comparison](../../reliability/features/native-signal/idle-service-cadence.md#four-arm-comparison-with-idle-reuse) completes four arms but retains a sampled slowdown of approximately 5.2% versus a physical reduction of approximately 49.8%.
Repeated idle service is removed from most checks. Owner draining and publication still precede the guard.
The [owner-drain diagnostic](../../reliability/features/native-signal/owner-drain-cost.md) measures a median paired 38.414 ms across 500,000 calls without application work or a peer.
It does not isolate the full-workload cost. The [8192 interval](../../reliability/features/native-signal/sparse-probe-interval.md) passes controlled Linux countdown and bound payment checks.
The [8192 four-arm comparison](../../reliability/features/native-signal/sparse-probe-comparison.md) now binds its plan to runtime interval evidence and preserves default 1024.
It completes but remains unqualified, with a 13.90%–14.43% sampled slowdown against a 46.56%–46.58% physical reduction.
Next, investigate first-use receipt work and recurring callback costs. The first peer request bracket is 27.378076 ms, without proof of its cause.
Keep all payment bounds and frozen ownership limits unchanged.
Source membership and instrumentation attribution require separate contracts before prediction qualification.
Do not repeat the unchanged controller comparison as a substitute for those contracts.

1. Specify global and per-thread delay accounting, blocked intervals, wake outcomes, and thread lifecycle.
2. Build a narrow CLR startup profiler spike with selective IL rewriting and original source maps.
3. Test long-loop probe coverage, disabled-probe overhead, zero-speedup overhead, and JIT effects.
4. Combine CPU sampling with cooperative delay checks. Keep platform sampling constraints explicit.
5. Compare sampled predictions with real CPU optimizations. Preserve raw observations and failed predictions.
   Done for the [target-local fork/join model](target-local-prediction.md). Other workloads remain open.
6. Support named synchronization primitives only after dependency-wake and timeout accounting pass.
7. Add ReJIT and verify active-frame, inlining, source-map, and compilation-settling constraints.
8. Separate request lifetime from CPU execution before async support.

IL rewriting supplies intervention points. It does not supply CPU sampling or synchronization semantics.
The controlled Linux countdown comparison passes its accounting checks and substantially reduces dense-probe cost.
Native-range attribution remains unresolved for inline countdown instructions.
Raw rewritten-IL diagnostics separate the inspected loop ranges with empty-stack gate placement on both controlled methods.
Candidate exclusion and whole-method attribution both fail the physical-prediction gate in the controlled countdown loop.
The stage comparison identifies injected probes as the largest measured cost in the controlled loop.
Investigate a lower-cost intervention mechanism before changing sampled accounting.
A native signal experiment must measure GC effects and bound handler work before any claim of CLR safety.
The [bounded signal workload](../profiling/signal-intervention.md) passes the initial arithmetic and forced-GC checks.
The [transition workload](../profiling/signal-transitions.md) passes JIT, blocked-wait, managed/native-call, and pending-shutdown checks.
Keep abnormal target exit as an explicit process-lifetime limit.
The [signal accounting model](../profiling/signal-accounting.md) supplies bounded requests, matched acknowledgments, and required sampling epochs.
The [live sampled signal controller](../profiling/sampled-signals.md) connects CPU events to bounded requests.
Its first run generates causal credit and completes protocol checks, but fails the observed unpaid-debt limit.
Investigate overshoot settlement and sampling exclusions before adding physical-prediction checks.
Sample-driven selected checks recover credit in the cold experiment but retain its startup debt failure.
The first 250 ms warmup experiment fails the handler-duration bound. Keep this failure and investigate wait overshoot.
The three-block warm comparison passes all protocol checks but fails capacity in two of three 50-percent cases.
Measure wait overshoot, service gaps, and debt age before selecting another intervention actuator.
The [service timing run](../../reliability/features/native-signal/service-timing.md) exposes both wait overshoot and long pending requests that perform no wait.
The [native phase run](../../reliability/features/native-signal/phase-timing.md) places most of one long acknowledgment before the first handler clock reading.
Two of that run's three 50-percent cases pass capacity. A separate delay case still exceeds the 5 ms handler limit.
Investigate target scheduling and CLR suspension before choosing between delivery and actuator changes.
The [GC counter comparison](../../reliability/features/native-signal/gc-and-ring-cache.md) shows substantial controller allocation even after empty ring drains reuse snapshots.
Inspect repeated accounting work during idle polling. Keep GC causation and sampled capacity unresolved.
The [peer-cadence run](../../reliability/features/native-signal/peer-cadence.md) skips unchanged-state preparation and lowers allocation in completed cases.
It still fails one warmup handler bound and both completed 50-percent debt checks.
The [direct accounting read](../../reliability/features/native-signal/accounting-read.md) removes the full controller's per-iteration duration wrapper allocation.
Two treatment cases still exceed the handler limit during native waits.
The [wait comparison](../../reliability/features/native-signal/wait-comparison.md) passes three sampled spin treatments at higher process CPU cost.
The [spin reliability run](../../reliability/features/native-signal/spin-reliability.md) passes all transition and sampled protocol checks but retains two forced-GC debt failures.
The [combined trace](../../reliability/features/native-signal/scheduler-runtime.md) correlates runtime suspension and scheduler records while retaining a forced-GC capacity failure.
The next [native service prototype](native-debt-service.md) will test finite peer leases with separate bounded native requests.
Keep physical-change prediction as a separate unmet gate.
Keep acknowledgment polling continuous and distinguish signal delivery time from native wait time.
The original signal and transition experiments generate no causal credits.
See [component overhead](../../reliability/features/linux-perf/component-overhead.md) and [signal research](../../docsSrc/Research/native-signal-intervention.md).
See [attribution sensitivity](../../reliability/features/linux-perf/attribution-sensitivity.md).
Keep mixed boundaries and debugger limitations explicit. See [the attribution investigation](../../docsSrc/Research/injected-cpu-attribution.md).
The hybrid design remains provisional until measured coverage and overhead support it.
See [the port decision](../../docsSrc/Research/coz-port-and-il-rewriting.md) and [the implementation comparison](../../docsSrc/Research/managed-coz-implementations.md).

```powershell
dotnet run --project samples/CozNet.Serial -c Release -- artifacts/serial
```

The sample's initial acceptance target is two percentage points of prediction error.
One machine cannot establish general accuracy. Retain failures and compare independent runs.
The [current physical run](../../reliability/features/serial/README.md) exceeds this target for the 50% case.
Investigate prediction stability separately. Runtime experiments do not expand the library's supported workload model.

```mermaid
flowchart LR
    CozContracts --> DelayLedger --> RuntimeSpike --> Validity --> Synchronization --> Async
    RuntimeSpike --> Sampling
    RuntimeSpike --> ILProbes
    Serial --> SerialValidity
```

See [current engine](../profiling/summary.md) and [research](../../docsSrc/Research/causal-profiling-dotnet.md).
