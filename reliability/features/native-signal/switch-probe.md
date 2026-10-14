# Scheduler record capability

A bounded native probe captures per-thread scheduler records on Linux x64.
This result supports a diagnostic collector. It does not explain the retained signal delays.
The probe uses no CLR or signal handler and changes no delay accounting.

## Method

The calling thread opens a disabled `PERF_COUNT_SW_DUMMY` event for itself.
It requests `context_switch`, `sample_id_all`, `PERF_SAMPLE_TID | PERF_SAMPLE_TIME`, and `CLOCK_MONOTONIC`.
It excludes kernel and hypervisor counting and requests `PERF_FORMAT_LOST`.
The native UAPI attribute has 136 bytes in the build image.
The running kernel accepts it with unused fields zeroed.

The thread enables collection, sleeps repeatedly, runs a 200 ms busy loop, and disables collection.
Monotonic timestamps bracket both control calls and the workload phases.
The probe drains the ring after disabling the event, then reads its final loss counter.
The thread stays alive until the mapping and descriptor close.
Each process has a ten-second watchdog.

The normal case requests 100 one-millisecond sleeps and uses a 256 KiB ring.
The overflow case requests 4,000 sleeps of 50 microseconds and uses a 4 KiB ring.
Neither case drains during the workload.
Requested sleep times are not measured sleep durations.

The Python decoder checks record type, size, identity, ordered timestamps, and switch flags.
It pairs switch-out with switch-in and rejects repeated transitions without reported loss.
Loss resets pairing state. Unmatched boundary records remain censored.
Seven deterministic checks cover malformed records, identity, flags, ordering, loss, and censored boundaries.
The separate concurrent run below covers live wrapped records and repeated draining.

## Evidence

The [archive](data/switch-probe/run-20261014T044639391.zip) preserves source, executable, runner, raw records, summaries, decoder, and checks.
All 17 archived files match their retained originals.
The archive SHA256 is `3818f3bb596413ff0bdcf4d98823d92b430445b981a2378864dec04c2a844817`.
The environment file records source and executable hashes, image identity, kernel version, and process exit codes.
The kernel is `5.15.153.1-microsoft-standard-WSL2`.

| Case | Exit | Switch records | Paired intervals | Preemption flags | Loss records | Final lost count |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Default Docker policy | 2 | Unavailable | Unavailable | Unavailable | Unavailable | Unavailable |
| Diagnostic policy, normal | 0 | 204 | 102 | 2 | 0 | 0 |
| Diagnostic policy, overflow | 0 | 170 | 85 | 0 | 0 | 7,832 |

Default Docker policy denies `perf_event_open` with `errno=1`.
The diagnostic cases use `--security-opt seccomp=unconfined`, with no network and all capabilities dropped.
They do not change host policy.

The normal case has 107,701,834 ns across its complete observed switched-out intervals.
Its stream has no reported loss or censored record pair.
This does not establish precise scheduling boundaries at enable or disable.

The overflow case demonstrates trailing loss without a later `PERF_RECORD_LOST` record.
Its observed prefix cannot describe the whole workload.
The final counter is therefore required before a collector claims complete coverage.

## Concurrent ring collection

The [concurrent archive](data/switch-probe/run-20261014T044941757.zip) preserves a separate source snapshot and one new workload case.
All 13 archived files match their retained originals.
The archive SHA256 is `d2da2710664c61953f198e48f392d166cd1f9c73e4217c651955d107bacaab3d`.
The target requests 4,000 sleeps of 50 microseconds, then runs the same 200 ms busy phase.
A second thread drains the 4 KiB ring and requests a 100 microsecond sleep between drains.
Actual collector intervals depend on scheduling. This run does not establish a reliable polling deadline.
Output storage is bounded at 4 MiB and allocated before collection starts.
The target disables its event, stops and joins the collector, drains remaining records, then reads the final loss counter.
The target remains alive throughout cleanup.

The collector acquires `data_head`, copies complete records, and releases `data_tail` after copying.
It rejects invalid headers, head/tail distances, and exhausted output storage.
Before opening the event, 384 deterministic cases check ring copies at all byte offsets across two ring cycles.
The cases cover 8-byte headers, 24-byte switch records, and 40-byte loss records.

The run captures 8,000 switch records and 4,000 complete switched-out intervals.
It performs 2,762 nonempty drains and copies 31 records across the ring boundary.
The final lost count and emitted loss count are both zero.
The decoder accepts every record's size, identity, ordering, and transition sequence.
The seven decoder checks also pass against this snapshot.

This establishes basic concurrent transport on the current kernel.
It does not cover abort cleanup, target retirement races, collector scheduling stalls, or managed integration.
The archive preserves the observed result without repeating the workload to obtain a pass.

## Limits and next step

The probe establishes event access, basic decoding, and final loss detection on this kernel.
It does not measure collection overhead or distinguish blocked time from runnable waiting.
The preemption flag does not identify a competing thread or a wakeup instant.
No result establishes exact signal-handler entry or CLR safety.

Next, add optional controller and target collection behind a Linux backend.
Keep registration identity, bounded storage, loss checks, and monotonic boundary contracts explicit.
Measure observer cost before using these records to interpret request delays.
Keep all intervention limits and the two-percentage-point prediction gate unchanged.

See the [Linux perf contract](../../../lode/profiling/linux-perf.md) and [target CPU evidence](target-cpu.md).
The [matching kernel header](https://github.com/microsoft/WSL2-Linux-Kernel/blob/linux-msft-wsl-5.15.153.1/include/uapi/linux/perf_event.h) defines the record contract.
The [matching ring implementation](https://github.com/microsoft/WSL2-Linux-Kernel/blob/linux-msft-wsl-5.15.153.1/kernel/events/ring_buffer.c) accounts for failed output reservations.
