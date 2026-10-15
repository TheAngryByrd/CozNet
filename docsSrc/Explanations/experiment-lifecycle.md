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
