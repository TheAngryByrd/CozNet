# Project scope

An opt-in [selected scheduling handoff](profiling/selected-handoff.md) preserves debt accounting and attempts one yield at a qualifying selected checkpoint.
Its first CPU 0 treatment fails the handler bound while its control passes.
The policy remains disabled by default. It does not establish a capacity or prediction fix.

Opt-in [bound scheduler capture](profiling/bound-scheduler.md) now correlates peer progress with three owner-bound scheduler streams.
One constrained treatment fails peer debt while its request-to-debt window lies inside an observed peer switch-out interval.
Clock assumptions remain explicit. Accounting limits remain unchanged, and stable capacity is unverified.

An opt-in [native progress marker](profiling/cooperative-progress-marker.md) now captures peer submission and failed-debt observations.
Its unrestricted treatment fails the receipt limit. A one-CPU run captures pending markers after two failed debt gates.
The markers do not establish why the peer did not reach a native poll.

The [peer-progress analyzer](profiling/peer-progress.md) places one direct trial's first child after its debt observation under explicit clock-rate assumptions.
A later retired journal does not establish payment before the gate. Peer scheduling remains unresolved.

An opt-in [receipt-readiness query](profiling/receipt-readiness.md) replaces normal controller header polling with atomic observation.
Full receipt validation and cleanup retain controller ownership. Capacity and overhead benefits remain unverified.
Its two one-CPU treatments fail peer debt earlier than the interleaved controller trials. Controller polling remains the default.

An opt-in [dispatch diagnostic](profiling/controller-dispatch.md) retains controller queue, action, and return brackets without changing payment rules.
Its one-CPU failure separates a short failed receipt-read action from a 115.427269 ms caller-observation interval. The cause of that interval remains unresolved.

An opt-in [batch service comparison](profiling/sample-publication.md) changes peer service cadence while preserving every per-sample debt gate.
Both batch treatments fail peer debt in the retained one-CPU comparison. Stable capacity and prediction remain unverified.

CozNet is an experimental F# library for .NET 10. The public engine compares proportional delays in serial CPU regions. It records randomized blocks and paired elapsed-time reductions. An internal immutable ledger models sampled Coz delay credits, thread lifecycle, and dependency wakes. An internal cooperative adapter performs measured waits from this ledger. A controlled Linux workload connects sampled method periods to this adapter. The public API requires explicit regions and progress. The public engine does not support parallel, async, or native sampling. General application accuracy remains unverified. The repository uses MiniScaffold as its basis and `master` as its default branch. The checksum sample compares predictions with actual reductions in work.

```fsharp
session.Region(region, fun () -> cpuWork ())
session.Progress()
```

```mermaid
flowchart LR
    Settings --> Regions --> Observations --> Estimates
```

A separate Windows and Linux x64 [CLR probe prototype](profiling/runtime-probes.md) verifies selective IL rewriting in a controlled loop.
An internal [cooperative runtime](profiling/cooperative-runtime.md) connects the ledger to measured managed waits and cancellation recovery.
The CLR prototype also verifies asynchronous stack snapshots through a Windows backend behind a shared sampler interface.
It does not extend the public engine's supported workload model.
A separate [Linux task-clock workload](profiling/linux-perf.md) verifies per-thread CPU events in a container with the required syscall access.
Those events resolve against a fixed managed method map and drive credits at manual or injected probes in a controlled two-thread workload.
Dense injected probes have excessive cost. Countdown probes reduce that cost in the controlled Linux comparison.
Inline countdown instructions remain inside selected-method CPU attribution. Exact profiler-CPU exclusion remains unresolved.
An optional raw-map sensitivity diagnostic excludes injected IL candidates. Both diagnostic modes still fail the physical-prediction gate.
See [the retained comparison](../reliability/features/linux-perf/attribution-sensitivity.md).
The [component comparison](../reliability/features/linux-perf/component-overhead.md) identifies injected probes as the largest measured overhead stage in the tiny loop.
A separate [signal experiment](profiling/signal-intervention.md) passes bounded arithmetic and forced-GC cases on Linux x64.
General CLR safety and sampled signal-driven prediction remain unverified.
A separate [scheduler probe](profiling/scheduler-records.md) captures per-thread switch records on the current Linux kernel.
Its overflow case requires the final loss counter to detect missing records.
The optional scheduler route captures the controller and both targets with zero reported loss in four Linux cases.
The GC treatment still fails the debt limit. Its delayed requests overlap observed target switch-out intervals.
Blocking cause, observer cost, and prediction accuracy remain unresolved.
The [combined scheduler and runtime trace](../reliability/features/native-signal/scheduler-runtime.md) preserves a forced-GC capacity failure with valid correlation.
A long peer request has no enclosing runtime suspension overlap.
The retained product maps to runtime sources where restart releases barriers without waiting for each worker to execute.
Neither restart nor observed off-CPU time justifies debt forgiveness.
An internal [signal accounting model](profiling/signal-accounting.md) connects sample credit to bounded requests and matched acknowledgments.
An internal [peer service model](profiling/signal-service.md) adds finite leases and exact-once aggregate settlement of bounded child journals.
Its [portable native owner](profiling/native-service-owner.md) passes fake-transport checks on Windows and Linux.
Its [Linux transport](profiling/linux-service-transport.md) passes controlled native delivery and lifetime checks.
Its [C ABI and managed client](profiling/service-interop.md) pass controlled receipt and lifetime checks.
Its [ledger adapter](profiling/service-accounting-adapter.md) passes pure and controlled live journal checks.
Its sampled native service path now runs through both adapters. The first zero-speedup case passes, but treatment fails during warmup at 19,999,953 ns of peer debt. See [the retained service run](../reliability/features/native-signal/sampled-service/README.md).
Its deterministic replay checks cover debt settlement and sampling epochs.
The [live sampled signal workload](profiling/sampled-signals.md) connects these components, but its first run fails the observed debt limit.
Sample-driven selected checks recover credit. The first warmup run then fails the handler-duration bound.
The native phase comparison passes two of three 50-percent capacity cases and all protocol checks.
Wait overshoot and service stability remain unresolved.
The [service timing records](../reliability/features/native-signal/service-timing.md) expose long pending requests that perform no native wait.
The [native phase records](../reliability/features/native-signal/phase-timing.md) place most of one long request before the first handler clock reading.
The [allocation comparison](../reliability/features/native-signal/gc-and-ring-cache.md) removes empty-drain allocation but still exposes gigabytes of controller allocation and a capacity failure.
The [peer cadence](../reliability/features/native-signal/peer-cadence.md) skips redundant accounting preparation and reduces allocation in completed cases.
Its retained run still fails handler or debt limits in every 50-percent case.
The [direct accounting read](../reliability/features/native-signal/accounting-read.md) removes a wrapper allocation from the full controller's generated code.
Zero-percent allocation falls to about 5.17 MiB. Two treatment cases still exceed the handler limit during native waits.
The [wait comparison](../reliability/features/native-signal/wait-comparison.md) passes three sampled spin treatments but fails all three sleep treatments.
Spin increases measured process CPU.
The [spin reliability run](../reliability/features/native-signal/spin-reliability.md) passes all runtime transitions and sampled protocol checks.
Two forced-GC treatments fail capacity. Physical-change prediction remains unverified.
An explicit [diagnostic trace path](profiling/runtime-suspension-trace.md) records bracketed EventPipe clock markers.
An explicit abort marker permits correlation of recorded failed runs while preserving workload failure.
Deterministic warmup and measurement failure fixtures pass through Linux EventPipe and on Windows.
Its first local capture has consistent clock alignment and zero reported event loss.
The correlation finds 104 complete suspension episodes and 53 overlapping measurement requests. It does not explain the earlier capacity failures.
The subsequent traced comparison retains four failed treatments. A long acknowledgment-observation delay has no runtime suspension overlap.
Controller work and scheduling remain unresolved. One handler-bound failure has an incomplete marker sequence and fails correlation validity.
A [controller CPU diagnostic](../reliability/features/native-signal/controller-cpu.md) retains two capacity failures in twelve cases.
One peak-debt request spends 12.97 ms before the first handler clock reading while the controller remains active.
Target scheduling, signal delivery, and handler-prefix execution or descheduling remain unresolved. Controller starvation does not explain that request.
General eligibility, source-line mapping, and prediction accuracy remain unverified.
An optional [target CPU diagnostic](profiling/target-cpu-observations.md) captures remote CPU readings around requests without changing the handler.
Its native contract checks pass. The first capture retains two treatment failures and five inconsistent target CPU records.
Clock accuracy, diagnostic overhead, and target execution bounds remain unverified.

See [engine contracts](profiling/summary.md), [sampled accounting](profiling/delay-ledger.md), [practices](practices.md), and [future work](plans/roadmap.md).


The native cooperative transport has an optional [child CPU journal](profiling/cooperative-interop.md#optional-cpu-records).
Linux records owner-thread CPU readings around admitted wait attempts. Other platforms report unsupported capability.
The separate diagnostic snapshot preserves failed children and requires mailbox quiescence.
Managed capture and selected-payment replay retain these records, including failed service evidence.
CPU observations do not replace wall-time payment.

The [single-CPU cooperative diagnostic](../reliability/features/native-signal/cooperative-single-cpu.md) retains a peer debt failure after 8538 selected batches.
Its late credits come from buffered samples, and the pending peer request has no child in its first failure snapshot.
No handler exceeds 5 ms. An opt-in owner-driven collector now drains before selected debt checks. Its first ordinary-CPU-set run passes all three cases, while the background treatment fails selected debt. Stable capacity and isolated overhead remain unverified.

The [one-CPU owner/background comparison](../reliability/features/native-signal/owner-drain-single-cpu.md) fails all four treatments with verified evidence.
One owner failure accrues new credit after peer arming. Another rejects a managed receipt interval without retaining its exact endpoint.
Owner draining does not establish stable service capacity.

The bound sampled workload retains rejected managed receipt interval arguments and request identity.
Controlled Windows/Linux checks cover both slots. The first new constrained capture fails peer debt before exercising live receipt rejection.
