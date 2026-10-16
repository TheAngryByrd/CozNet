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

## What remains

Moving decoding alone does not move credit or payment ownership.
The existing timer backend still requires managed reservations for finite native leases.
A target-local source needs bounded notification, source disable and final drain, epoch continuity, code invalidation, and safe retirement.
Native accounting also needs exact-once reconciliation with managed evidence and measured wall-time payment.
Unread backlog cannot become silently dropped credit or a retrospective change to the experiment.

These contracts must precede a live target-local backend. The current decoder does not establish CLR safety, stable capacity, or prediction accuracy.
IL rewriting remains a separate cooperative intervention option with its own coverage and observer-cost tradeoffs.
Target-local native processing is not yet evidence that those tradeoffs can be avoided in a general .NET application.
