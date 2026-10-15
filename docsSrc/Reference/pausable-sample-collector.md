# Pausable sample collector

`PausableSampleCollector` is an internal drain-ownership component for selected-owner payment.
`BoundSampleCollection.StartPausable` uses it for excluded owner callbacks.
The legacy peer-only sampled payment command uses the existing one-shot source contract.

## Control operations

The constructing thread owns `RequestPause`, `Resume`, `RequestStop`, and `Complete`.
One dedicated worker calls the supplied drain operation.

`RequestPause()` returns a task for a pause handle.
The worker finishes any current drain and performs a final drain before it returns that handle.
The worker does not drain while the pause remains active.

`Resume(handle)` requires the exact current handle from this collector.
A foreign handle, an old handle, or a second resume fails.
The pause sequence increases after each successful pause.

`RequestStop()` revokes a pending pause and returns a completion task.
A running collector performs a final drain before it stops.
A paused collector stops without another drain because its caller can have closed the old admission epoch.
Stop cannot return a pause handle that permits later resume.

`Complete()` joins the worker and releases the wake event.
A drain exception reaches the pending pause task and completion with the original exception object.
Disposal after completion does not repeat cleanup.

## Source responsibilities

The collector does not enable or disable CPU events and does not assign eligibility epochs.
Its caller must disable the source before requesting a pause.
Only a successful pause acknowledgement permits closure of the old sample admission epoch.
The caller must complete excluded payment before it opens the next epoch and resumes collection.

The caller must retain cumulative loss evidence across pauses.
A loss record delivered after resume can describe loss before the pause.
Any later loss rejects the whole experiment. Rejection cannot reverse completed waits.

The collector cannot cancel a blocked drain operation.
Callers need a process watchdog and must preserve source ownership until the worker stops.
These checks do not establish live perf pause semantics, CLR safety, payment capacity, or prediction accuracy.

See [sample eligibility](../Research/cooperative-sample-eligibility.md) for the source and eligibility epoch distinction.
See [perf pause ownership](../Research/perf-pause-ownership.md) for the restricted task-clock source analysis.
See [bound CPU sessions](bound-cpu-session.md) for the separate counter-journal parser.

## Owner-driven collection

`IPausableSampleCollector` defines the shared pause and completion contract.
`CollectorPause` is the common token. Resume requires the current token by reference identity.
`PausableSampleCollector` retains its background worker.
`OwnerSampleCollector` performs each drain synchronously on its constructing thread.
It does not start a worker or drain during construction.

`BoundSampleCollection.StartOwnerPausable` selects the owner collector.
`DrainOnOwner()` performs one drain before the caller reads provisional samples.
It rejects foreign threads, excluded execution, completed cleanup, and other collector modes.
Recursive drains, excluded execution, and cleanup fail while an owner drain remains active.
A drain failure permanently rejects further drains and preserves the original exception.

The caller stops the producer before requesting pause.
The owner collector then performs the final drain and returns its pause token.
Completion drains a running collector once. Completion does not drain a paused or failed collector.
For this mode, `CollectorJoined` means that no drain retains source ownership. No worker exists to join.

The selected-payment workload accepts `COZNET_OWNER_DRAIN=1` only with an explicit selected cadence.
Unset and `0` select background collection. Other values fail.
The runner exposes this setting through `-OwnerDrain` with `-SelectedPayment` and `-SelectedCadence`.
The owner drains before the sample-count check at selected probes and after each selected batch.
The Linux ring reader processes only the head captured at drain entry.

This mode changes publication timing. The credit rule, payment exclusion, and timing bounds remain unchanged.
Owner drain CPU runs on the sampled thread and can enter selected sample periods.
`profilerCpuExcluded` remains false.
`DrainAcknowledged` includes the synchronous final drain in this mode.
`Dispose()` calls `Complete()` and can drain a running source or throw its stored failure.
Its effect on capacity, overhead, and prediction accuracy remains unverified.
