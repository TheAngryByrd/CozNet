# Native signal intervention on CoreCLR

Date: 2026-10-13.
Runtime revision: `60629d14374c56f1cb51819049ad1fa529307f8d`.
Coz revision: `256e3c389424d6a1691ee5d18b5ecb5a6c1001ec`.
This is source research. It does not verify a signal handler, GC interaction, or runtime safety experimentally.

## Recommendation

Run a bounded Linux x64 feasibility experiment with a dedicated native signal handler and no IL rewriting.
Restrict intervention to fixed, nonallocating arithmetic methods on registered threads.
Use preallocated native state and a strictly limited delay request.
Do not claim that this mechanism safely profiles arbitrary managed execution.

The sources establish a major distinction: POSIX signal safety does not establish CLR suspension safety.
A foreign signal handler does not automatically switch an interrupted managed thread into preemptive GC mode.
CoreCLR's own signal mechanism checks runtime state and uses GC-aware frames or return-address hijacking. [R1][R2]
The proposed experiment must test the resulting GC delay and fail explicitly when its bounds are exceeded.

The current countdown experiment still fails prediction accuracy after candidate CPU exclusion. [L1]
Removing its loop gate is a useful new experiment, but native service overhead and attribution errors will remain possible.

## Native Coz mechanism

Native Coz uses `SIGPROF`, a 1 ms perf sample period, and batches of ten samples. [C1]
Its per-thread processing timer uses `CLOCK_THREAD_CPUTIME_ID` and `SIGEV_THREAD_ID`. [C2]
The Linux perf event collects instruction pointers and call chains separately from timer delivery. [C3]
Thus a service signal is not itself the CPU sample used for virtual credit.

The signal handler locates thread state, rejects reentrant use, processes recorded samples, and settles delay.
Selected samples increase local delay. A thread behind global delay waits and credits its measured wait.
The sampler stops around the inserted wait. [C4][C5]
Coz registers its signal handler with `SA_SIGINFO | SA_ONSTACK`. [C6]

This is evidence that native intervention can avoid application backedge calls.
It is not proof that Coz's handler operations, state structures, or thread interception satisfy CoreCLR's contracts.
Do not copy the handler wholesale or run its managed equivalent inside a signal.

## CoreCLR constraints

### Signal ownership and chaining

On the reviewed Linux configuration, CoreCLR uses `SIGRTMIN` for activation, with `SIGUSR1` as the source fallback when real-time signals are unavailable. [R3]
Its PAL also installs handlers for hardware exceptions and selected process-control signals. [R10]
`System.Native` maintains another signal-dispatch layer for managed registrations and process handling. [R4]
A signal that is unused by PAL today is not necessarily unused by the application or another profiler.

Choose a dedicated signal only after inspecting its current disposition and the runtime configuration.
Do not replace the activation signal, fault handlers, debugger traps, or an existing application handler.
For the first controlled process, fail on an occupied signal instead of implementing generic chaining.
Installation, process-wide disposition, per-thread masks, pending delivery, and teardown require an explicit ownership contract. [P1][P2]

CoreCLR saves previous dispositions and has specific logic for `SA_SIGINFO`, ignored/default actions, and resending signals. [R5]
Blindly calling a previous function pointer is not a complete general chaining implementation.
The `PerfMapIgnoreSignal` configuration can also install a handler for a profiler marker signal. [R6]
Check it when selecting the experiment signal.

### GC mode and safepoints

CoreCLR defines cooperative mode as a state in which the thread can modify GC references.
The collector must bring that thread to a safe location before enumerating its roots.
Its documented internal invariant forbids arbitrary blocking native work while cooperative. [R1]
An ordinary P/Invoke transition can change mode; asynchronous signal delivery is not that transition.

The runtime activation handler invokes `CheckActivationSafePoint` before its runtime callback.
That check requires managed code, cooperative mode, and no debugger single-stepping. [R2][R7]
`HandleSuspensionForInterruptedThread` then checks `IsGcSafe`.
At a safe point, it pushes `RedirectedThreadFrame` and pulses GC mode.
Otherwise, it can hijack a managed return address. [R2]
These internal operations are not a public profiler service for a foreign handler.

A foreign handler sleeping after interrupting managed code leaves that thread's GC mode unchanged.
If runtime activation arrives while the instruction pointer is inside that native handler, the managed-code activation check does not pass. [R2]
The GC may have to wait until the handler returns and the thread reaches a usable runtime safe point.
A bounded pause without locks can therefore behave like a scheduling delay, but it can still extend GC suspension latency.
The reviewed code does not prove that arbitrary handler sleeps are safe across all runtime states.

Do not read private `Thread` fields, patch return addresses, call `PulseGCMode`, or invoke CLR profiling APIs inside the handler.
The current IP-range restriction is an experimental eligibility filter, not proof that the interrupted point is a GC safepoint.

### Signal masks and alternate stacks

Signal delivery normally blocks that signal while its handler executes. Additional `sa_mask` entries also apply. [P2]
Do not enable `SA_NODEFER` or accumulate recursive profiler handlers in the first experiment.
Do not block all signals: that would also block CoreCLR activation.

`SA_ONSTACK` requires particular care.
CoreCLR explicitly blocks activation while its own alternate-stack handler runs, to avoid overflowing that stack.
Its exception path unmasks activation after returning to the original stack. [R11]
Coz's `SA_ONSTACK` choice does not transfer this protocol to a custom CLR handler.
For the initial deep-stack-free arithmetic workload, use the normal stack and reject stack-overflow scenarios from supported scope.
Any later alternate-stack design needs separate runtime and nested-signal tests.

### Handler operations and wait syscalls

POSIX permits `clock_gettime`, `write`, and specific other functions in signal handlers.
It permits lock-free C atomic operations under its stated conditions.
It requires saving and restoring `errno` when the handler changes it. [P3]
No allocation, managed callback, logging formatter, mutex, condition variable, lazy initialization, or unbounded spin loop belongs in this handler.

Neither `nanosleep` nor `clock_nanosleep` appears in POSIX.1-2024's required async-signal-safe list. [P3]
The inspected glibc 2.39 implementation routes clock sleep through cancellation-aware syscall machinery. [G1]
Therefore, calling ordinary libc sleep is not established safe merely because Coz does it.
A Linux x64 experiment needs an audited direct-syscall wait implementation, or a separately proved platform-specific safe implementation.
This is a Linux ABI dependency, not a portable C++ promise.

Use a monotonic absolute deadline and a fixed maximum service interval.
An interrupted sleep must not restart indefinitely or wait for another managed thread to make progress. [P4]
Measure actual wait and handler residence separately.
Inspect generated native code for unexpected TLS resolver, allocation, cancellation, or out-of-line atomic calls.
Preinitialize and keep thread slots alive until all possible handler use has ended.

## Feasible bounded experiment

The following design is proposed, not verified:

1. Use one disposable Linux x64 process, fixed CoreCLR version, and two registered dedicated managed threads.
2. Warm and freeze the nonallocating arithmetic methods and their native ranges. Disable tiering and ReadyToRun for this experiment.
3. Register native thread slots through ordinary calls before measurement. Keep allocations and CLR callbacks outside the handler.
4. Install a dedicated signal only when its disposition is unused. Verify that registered threads leave it unblocked.
5. Keep perf CPU sampling and event validation on the existing collector path. Do not credit the service signal's interrupted IP as a CPU sample.
6. Publish native delay requests through audited lock-free state. Keep signal delivery separate from the request's durable sequence number.
7. In the handler, verify sender provenance, thread generation, and interrupted x64 instruction pointer against an eligible fixed range.
8. Reject an ineligible interruption without waiting. Record deferred requests and maximum delivery/service gaps outside the handler.
9. Start with zero delay, then one bounded request such as 50 microseconds. Limit each invocation even when more debt exists.
10. Return normally without modifying the saved context. Never wait for a lock, allocation, managed callback, or controller response.
11. Stop sending requests, drain outstanding ownership, and join the target threads before reclaiming state.
12. Use an external process watchdog. Preserve timeout and crash evidence rather than retrying a failing process silently.

Signal masks and scheduling can postpone delivery, so a sender deadline is not a hard service-latency guarantee. [P1]
Thread-directed APIs still require correct thread lifetime management. `tgkill` targets a process/thread pair; it does not provide a reusable managed-thread identity. [P5]
Do not carry a stale thread identifier into a new registration generation.

## Required evidence before extending scope

- Compare zero-signal, zero-delay signals, and positive-delay signals against the same uninstrumented workload.
- Verify unchanged checksums, zero-credit behavior, monotonic request accounting, and bounded handler residence.
- Prove live selected credits and companion waits before workload completion. Final settlement alone is insufficient.
- Run an independent allocating thread that requests repeated collections. Record GC suspension and pause durations with and without signal service.
- Exercise signal arrival during JIT, managed/native transitions, blocked waits, thread exit, and profiler shutdown. Ineligible paths must skip intervention safely.
- Verify that an occupied signal and a blocked target signal fail or report unsupported operation explicitly.
- Check sample loss, throttling, handler/helper attribution, and unmapped addresses separately.
- Repeat physical-optimization prediction checks. Passing handler checks does not establish causal accuracy.

Handler CPU and native waits must not earn selected-work credit.
The signal's saved instruction pointer identifies interrupted application code, whereas a perf sample captured during handler execution identifies profiler work.
Do not substitute one for the other.
Do not relabel unknown samples as profiler overhead or subtract aggregate service time from elapsed duration. [L2]

## Extension boundaries

This experiment would establish only a Linux x64 backend for a restricted workload.
Other Linux architectures need verified `ucontext` layouts, syscall ABIs, atomic code generation, and runtime signal behavior.
Windows has no POSIX thread-directed signal equivalent for this design.
CoreCLR's Windows activation/APC and suspension paths are internal mechanisms with different constraints. [R9]
macOS uses different native sampling and signal paths, with additional platform restrictions in Coz and CoreCLR. [C5][R7]
Keep each intervention backend behind an explicit interface and retain cooperative probes where a native backend is unsupported.

No reviewed source confirms a general signal-only intervention contract for arbitrary CoreCLR threads.
The bounded experiment can answer whether this restricted route reduces probe cost without unacceptable GC disruption.

## Sources

[L1]: ../../reliability/features/linux-perf/attribution-sensitivity.md
[L2]: profiler-overhead-accounting.md
[C1]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.h#L39-L47
[C2]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/ccutil/timer.h#L17-L29
[C3]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L600-L623
[C4]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L886-L908
[C5]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L678-L758
[C6]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L112-L143
[R1]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/threads.h#L25-L105
[R2]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/threadsuspend.cpp#L5725-L5866
[R3]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/pal/src/exception/signal.cpp#L58-L62
[R4]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/native/libs/System.Native/pal_signal.c#L307-L360
[R5]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/pal/src/exception/signal.cpp#L381-L466
[R6]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/pal/src/exception/signal.cpp#L1030-L1047
[R7]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/pal/src/exception/signal.cpp#L897-L1002
[R8]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/pal/src/exception/signal.cpp#L1157-L1176
[R9]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/vm/threadsuspend.cpp#L5872-L5975
[P1]: https://man7.org/linux/man-pages/man7/signal.7.html
[P2]: https://man7.org/linux/man-pages/man2/sigaction.2.html
[P3]: https://pubs.opengroup.org/onlinepubs/9799919799/functions/V2_chap02.html#tag_16_04_03
[P4]: https://man7.org/linux/man-pages/man2/nanosleep.2.html
[P5]: https://man7.org/linux/man-pages/man2/tgkill.2.html
[G1]: https://github.com/bminor/glibc/blob/ef321e23c20eebc6d6fb4044425c00e6df27b05f/sysdeps/unix/sysv/linux/clock_nanosleep.c#L25-L72

[R10]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/pal/src/exception/signal.cpp#L181-L201
[R11]: https://github.com/dotnet/runtime/blob/60629d14374c56f1cb51819049ad1fa529307f8d/src/coreclr/pal/src/exception/signal.cpp#L563-L576
