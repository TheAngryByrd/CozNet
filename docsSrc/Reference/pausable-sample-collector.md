# Pausable sample collector

`PausableSampleCollector` is an internal drain-ownership component for selected-owner payment.
`BoundSampleCollection.StartPausable` uses it for excluded owner callbacks.
The sampled payment workload still uses the existing one-shot source contract.

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
