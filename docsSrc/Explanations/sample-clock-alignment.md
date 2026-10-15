# Sample time and measurement time

A CPU sample timestamp identifies when the kernel emits a sample.
A controller timestamp identifies when managed code reads or records it.
Delivery, buffering, scheduling, and controller work separate these observations.
Their difference does not establish a clock offset.

Linux perf uses `local_clock` by default.
CozNet now requests `CLOCK_MONOTONIC` explicitly for CPU samples.
The .NET 10 Linux `Stopwatch` implementation also uses `CLOCK_MONOTONIC`.
This common selection is necessary evidence, but it does not prove equal timestamps.

A Linux time namespace can add an offset to userspace monotonic readings.
The selected perf kernel clock does not apply that offset.
The kernel fast monotonic clock can also regress during timekeeping updates.
Thus, a common clock ID does not establish a timestamp error bound.

The sampled timer now records the calling thread's namespace and monotonic offset before and after measurement.
Bracketed native and managed readings check their observed mapping and units.
Missing evidence, changed namespaces, incompatible units, and clock regression prevent the consistency parser from accepting those readings.
Linux exposes namespace offsets for future children, which can use a different namespace from the caller.
The adapter rejects this difference instead of treating the child's offset as the caller's offset.

Two consistent observations do not bound every clock reading between them.
They also do not measure the difference between the kernel fast clock and userspace `CLOCK_MONOTONIC`.
A source-event correlation check and a justified uncertainty bound remain necessary.

Clock alignment also cannot establish the CPU interval represented by a sample.
A nominal task-clock period is not a measured elapsed CPU interval.
Disable and enable can preserve a remaining period across enabled intervals.
Missing samples and native journal tails still need independent bounds.

See the [CPU sample clock reference](../Reference/cpu-sample-clock.md) for implemented configuration.
See [matched physical prediction](matched-prediction.md) for the current accuracy limits.

## Sources

- [Linux perf event implementation](https://github.com/torvalds/linux/blob/v6.6/kernel/events/core.c): `perf_event_set_clock` and the task-clock timer.
- [.NET 10 Unix Stopwatch](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Stopwatch.Unix.cs) and [native timestamp implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/native/minipal/time.c).
- [Linux namespace clock offsets](https://github.com/torvalds/linux/blob/v6.6/lib/vdso/gettimeofday.c): `do_hres_timens`.
- [Linux offset reporting](https://github.com/torvalds/linux/blob/v6.6/kernel/time/namespace.c): `proc_timens_show_offsets` uses `timens_for_children_get`.
- [Linux fast monotonic clock](https://github.com/torvalds/linux/blob/v6.6/kernel/time/timekeeping.c): `ktime_get_mono_fast_ns`.
