# Experiment boundaries and unpublished delay

CozNet freezes progress and global delay before shutdown.
These counters describe accounting when the controller executes.
They do not assign each native wait to an exact source-time window.

The current sampled timer workload already separates initialization from measurement.
`Initialize` replaces the synthetic ledger, controller, stream identity, and counters before native measurement setup.
`MatchedCpuWorkers.Warmup` runs before the measured workers start.
The measured controller therefore starts without inherited synthetic leases.
This separation does not resolve the end boundary.

## Why wait totals cannot correct the endpoint

A sample first increases a thread's local credit.
`DelayLedger.catchUp` publishes a local lead to global delay.
A native child first enters the accepted service journal.
Lease release then adds the accumulated wait to local credit through `completeDelay`.
Another catch-up can publish a resulting local lead.

For example, suppose global delay is 100 ns and a thread's local delay is 80 ns.
An accepted 25 ns wait leaves global delay at 100 ns while the lease remains unsettled.
Release changes local delay to 105 ns.
A later catch-up can change global delay to 105 ns.
The accepted wait, settled local credit, and published global delay are different values.
This arithmetic example does not describe a measured run.

Concurrent worker waits cannot be summed into global delay.
Two workers can pay the same global increase independently.
Shutdown can also publish credit after the frozen progress reading.
Substituting shutdown state would combine different measurement windows.

## What other implementations establish

The pinned [Linux Coz implementation](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L678-L758) credits samples under the current experiment flag.
Its delay checks publish local leads and record measured waits locally.
Its [endpoint](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L359-L391) subtracts the global-delay delta from elapsed time.

The pinned [JCoz implementation](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L198-L245) clears its experiment flag, signals workers, and sleeps before reading global delay.
Its [handler](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L573-L667) credits matching stacks and periodically compares local and global delay.
These paths provide accounting precedents.
They do not establish a complete drain barrier or a retrospective source-window bound for CozNet.

## Capacity before endpoint qualification

The [capture without forced GC](../../reliability/features/native-signal/timer-scheduler-no-gc.md) fails the 2 ms debt gate before shutdown.
Four queued samples add 2 ms of local credit while a peer command remains active.
The subsequent catch-up publishes global delay before the failed checkpoint.
The first handler starts about 3.36 ms after the first arm reading.
All three guest scheduler streams show zero switch-out overlap from submission through the failed checkpoint.
This does not exclude host scheduling, clock effects, or unobserved execution costs.

The accepted prefix leaves 2,444,062 ns of debt at that checkpoint.
Crediting the entire elapsed prefix of the unfinished child would still leave at least 2,420,658 ns.
Later payment cannot qualify the earlier measurement.
A larger lease budget alone does not change that recorded prefix.
An implementation change must improve actual service or observation timing, then pass the existing capacity and physical-prediction gates.
Changing sample publication order requires a new comparison. It cannot retroactively repair this run.

## Cooperative delivery candidate

The next proposed capacity experiment uses existing method-entry and countdown probes to observe a finite request on the target thread.
Each probe would execute at most one bounded child wait, then return to application work.
This removes asynchronous timer delivery from that path. It does not guarantee a time bound between probes.
The existing ten-millisecond CPU cadence cannot gate these requests under the two-millisecond debt limit.

The request must retain worker, command, and generation identity until valid terminal settlement.
Existing native mailbox ownership can supply publication, claim, cancellation, and retirement rules.
A cooperative poll site needs explicit admission evidence. It is not an interrupted instruction pointer.
The portable lease core now checks a separate worker, code-version, and site identity through `arriveAtPoll`.
The [cooperative transport](../Reference/cooperative-target.md) binds the owner thread and provides explicit code-revocation guards.
Its [separate C ABI](../Reference/cooperative-interop.md) preserves site identity and captures native receipt timestamps.
The [managed boundary](../Reference/cooperative-managed.md) supports first receipt tracking and ledger binding for manual polls.
It still needs injected .NET probe binding and integration with actual CLR code lifetime.
This identity check does not prove runtime registration or execution at that site.
The [lease reference](../Reference/target-local-lease.md) describes the implemented contract.
The timer receipt contract cannot accept a fabricated address as proof of cooperative eligibility.

The experiment must retain the 50 µs child request, 2 ms lease, 5 ms child duration, and 100 ms receipt limits.
It must also retain the 2 ms debt gate and record zero-speedup instrumentation cost.
The [existing countdown comparison](../../reliability/features/linux-perf/component-overhead.md) found substantial probe overhead.
Removing timer delivery therefore does not establish prediction accuracy.
The [probe design comparison](../Research/probe-overhead-options.md) describes the armed-poll alternative and native Coz's different settlement cadence.
The .NET probe adapter is not implemented yet.

## Next prototype

The proposed lifecycle retains the existing estimator inputs: frozen global delay, elapsed time, and progress.
`DelayLedger.observe` now exposes settled local delay and global delay from one immutable ledger value.
The observation preserves runnable, delaying, and blocked states.
For blocked threads, it also preserves global delay at block entry.
Observation neither settles waits nor publishes credit.
`SignalAccounting.captureThread` combines this ledger observation with its current request or service ownership.
For a service, it retains accepted wait separately from settled local credit.
Release removes the service from this snapshot when it adds the accepted wait to local credit.
The sampled timer workload captures these observations at both frozen boundaries.
It rejects mismatched service identity, accepted prefix, or settlement state.
An accepted prefix can remain in controller ownership after ledger settlement if native release fails.
That prefix is no longer unsettled wait in the ledger observation.
Outstanding native work remains unknown until evidence bounds it.
An unknown value cannot become zero.

Boundary capture rejects unsupported accounting states.
An assessment of residual bounds remains planned before an accuracy claim is possible.
A requested wait duration cannot bound scheduler delay or actual wait overshoot.
Successful cleanup cannot prove physical equivalence.
Physical comparison must test the complete sampling and intervention behavior under the existing gates.

The complete lifecycle prototype is not implemented yet.
General application support and prediction accuracy remain unverified.
See [matched prediction](matched-prediction.md) and [the boundary reference](../Reference/measurement-boundaries.md) for current behavior.
