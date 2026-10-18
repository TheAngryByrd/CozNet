# Target-local sample processing

The [nominal timer diagnostic](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/timer-nominal-credit.md) records controller preemption while selected work continues.
Samples accumulate until the managed controller runs again. The resulting credit burst exceeds the existing debt bound.
This makes sample-processing ownership relevant to a .NET Coz backend.

## What upstream implementations establish

Linux native Coz drains each thread's perf ring on that sampled thread.
A thread CPU timer sends a targeted signal. The handler processes samples, publishes local delay, and pays outstanding debt.
Its central experiment thread does not perform each recurring drain.
See [the target timer](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/ccutil/timer.h),
[signal entry](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L887-L909),
and [sample processing and payment](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L678-L758).

Coz uses a 1 ms sample period and a regular batch of ten samples.
At 50% speedup, ten selected samples can publish 5 ms of credit.
Copying this cadence would conflict with CozNet's 2 ms debt gate.
See [the cadence constants](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.h#L40-L48).

JCoz instead uses a central sender to signal registered target threads.
Target handlers obtain JVM stack traces and periodically compare delay counters.
It does not provide a perf-ring reader. Its JVM-specific stack capture and synchronization cannot establish CLR handler safety.
See [signal dispatch](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L156-L220)
and [target handling](https://github.com/ByteLab-David/JCoz/blob/1df02b0f6e319bd7df05ba5451b1814791d1849f/src/native/profiler.cc#L573-L684).
Swift Coz and cozgo remain marker bindings in the [existing comparison](../Research/managed-coz-implementations.md).

## Why the reader needs a separate contract

The upstream Coz iterator snapshots the producer head, but this does not impose a small record or elapsed-time budget.
Its fixed stack buffer and record-copy path do not supply CozNet's malformed-record, loss, and throttle rejection contract.
Its source also comments out the ring fence based on thread-local use.
See [the iterator](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/perf.h#L180-L225)
and [record copying](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/perf.cpp#L153-L259).
The [Linux perf ABI](https://github.com/microsoft/WSL2-Linux-Kernel/blob/linux-msft-wsl-5.15.153.1/include/uapi/linux/perf_event.h) specifies barriers around head reads and tail publication.
Native signal-handler usage is not proof of safe execution inside an interrupted CLR thread.

The first CozNet component therefore decodes one bounded record into an owned value.
It uses portable C++ and leaves shared-memory ordering and dispatch to platform adapters.
Terminal errors prevent source repair or a later successful read from hiding earlier corruption.
See [the decoder reference](../Reference/native-perf-decoder.md).

## Why the live source reads the loss counter

A complete capture claim needs evidence that the kernel dropped no sample.
When the ring is full, the kernel counts each failed reservation.
It writes a `PERF_RECORD_LOST` record only when a later output succeeds.
If the ring stays full until disable, that record never appears.
Zero decoded loss records therefore cannot prove complete capture.

`PERF_FORMAT_LOST` makes `read(fd)` return the cumulative lost-sample count with the event count.
The same flag also adds that count to every `PERF_SAMPLE_READ` value in the ring.
Thus one source configuration changes both the final read and the sample length from 48 to 56 bytes.
The decoder declares each layout explicitly instead of inferring it from a record length.
The Linux adapter accepts only the 56-byte layout, so every live epoch can end with a final loss check.

The live overflow mode shows this case. It decodes the 73 samples that fit one page.
No loss record appears in the ring, and the final read reports positive loss.
A count-only source would show 73 valid samples and no sign of loss.

## Why each sample notifies its own thread

Upstream Coz arms a thread CPU timer on each target. CozNet first used the same trigger with a 2 ms interval.
On the verified kernel, those timers expired only about every 10 ms of thread CPU.
Each notification then held about ten 1 ms samples. At 50% speedup, ten selected samples can publish 5 ms of credit.
That burst conflicts with the 2 ms debt bound, independent of scheduling delay.

Perf can instead notify through fasync. With an owner thread and a real-time signal, the kernel requests one signal for each overflow.
Overflows that occur before its deferred wakeup work runs share one signal.
The signal arrives on the sampled thread and names the event descriptor.
In the live capture without masking, each notification holds one sample.
The [native ledger](../Reference/target-delay-ledger.md) then credits at most one selected sample in most dispatches.
The live capture records a median handler interval of less than 1 microsecond for a one-sample dispatch.

Queued notifications still need bounds. A thread can block the signal while samples accumulate.
The first dispatch after the mask then finds many samples, and the queued notifications follow it.
Each dispatch therefore has record and elapsed budgets. A budget stop reports remaining backlog instead of a completed drain.
See [the dispatch evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/target-dispatch.md).

## How the path connects

Moving decoding alone does not move credit or payment ownership. The target-local path moves all three onto the sampled thread.
The [dispatch reference](../Reference/target-sample-dispatch.md) covers bounded notification, source disable and final drain, epoch continuity, and native retirement.
The [ledger reference](../Reference/target-delay-ledger.md) covers credit, publication, and measured wall-time payment in the handler.
The [API reference](../Reference/target-local-api.md) covers the managed entry point and the exact-once managed replay of every journal.
Unread backlog cannot become silently dropped credit or a retrospective change to the experiment.

Managed threads run this path in the qualified fork/join model. See [target-local prediction](target-local-prediction.md) for its evidence and support boundary.
Code invalidation, blocking, GC-heavy work, and general CLR safety stay outside that model.
The older timer backend still requires managed reservations for finite native leases. It is a separate path.
