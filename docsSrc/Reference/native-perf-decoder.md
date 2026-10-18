# Native perf sample decoder

`native/service/PerfSampleRing.h` defines a portable C++20 decoder for two Linux task-clock record layouts.
`native/service/PerfRing.Linux.h` connects it to a live Linux x64 perf mapping.
Neither component is connected to a payment backend.

## Plan

`PerfSampleRingPlan::parse(bytes, process, thread, period, format)` returns an optional parsed plan.
The ring must contain at least 64 bytes and have a power-of-two size.
Process identity, thread identity, and nominal period must be positive.
`format` is `PerfSampleReadFormat::count` or `PerfSampleReadFormat::count_and_loss`. Other values reject.
The caller owns the ring memory and must keep it valid throughout reader lifetime.

`PerfSampleRingReader(plan, initialTail)` creates one consumer cursor.
The reader cannot be copied or moved. External ownership must prevent a second consumer of the same ring.

## Reading

`read(acquiredHead)` decodes at most one record and returns `PerfSampleRingRead`.
The method is `noexcept`. Its source uses no allocation, OS call, callback, lock, or symbol resolver.

| Result | Meaning |
| --- | --- |
| Sample present, error `none` | One complete sample was accepted. |
| No sample, error `none` | Accepted tail equals the supplied producer head. |
| No sample, error set | The reader is terminal. The first error remains on every later call. |

The sample owns its numeric values. It contains no pointer into the ring.
`acceptedTail()` advances by one record length only after all sample checks pass.
Failure preserves the previously accepted prefix. Corrected source bytes cannot repair a terminal reader.

## Supported record layout

The decoder reads little-endian fields and permits physical ring wrap at any byte position.
This wire format is independent of the decoder host. Live perf records must come from a little-endian producer.
Big-endian perf producers need a separate adapter or decoder.
The supported `PERF_RECORD_SAMPLE` layouts are:

| Offset | Width | Field |
| ---: | ---: | --- |
| 0 | 4 | Record type, `9` |
| 4 | 2 | Miscellaneous flags, user CPU mode required |
| 6 | 2 | Record length: `48` for `count`, `56` for `count_and_loss` |
| 8 | 8 | Instruction pointer |
| 16 | 4 | Process identity |
| 20 | 4 | Thread identity |
| 24 | 8 | Source timestamp |
| 32 | 8 | Nominal sample period |
| 40 | 8 | Cumulative CPU reading |
| 48 | 8 | Cumulative lost samples, `count_and_loss` only |

The plan declares one layout. A record with the other length fails with `sample_layout`.
In `count_and_loss`, a nonzero embedded loss counter fails with `lost_records` before acceptance.
The kernel writes a `PERF_RECORD_LOST` record before the next sample after a loss, so that record usually fails first.
The embedded check is a second guard. It is not the live loss signal. The final count/loss read is that signal.

Instruction pointers and timestamps must be positive. Process, thread, and period must match the parsed plan.
Timestamps and cumulative CPU readings cannot decrease. Equal readings remain valid because byte positions distinguish records.
Producer heads cannot decrease or exceed the ring capacity relative to the accepted tail.
Logical cursor rollover rejects. The reader does not treat rollover as a new stream.
Malformed lengths, loss records, throttle records, and unknown record types terminate acceptance.

## Platform obligations

The adapter must acquire the producer head according to the perf memory-order contract before decoding.
It must bind the source configuration to `PERF_SAMPLE_IP | PERF_SAMPLE_TID | PERF_SAMPLE_TIME | PERF_SAMPLE_PERIOD | PERF_SAMPLE_READ`.
`read_format = 0` produces the `count` layout. `read_format = PERF_FORMAT_LOST` produces the `count_and_loss` layout.
The decoder cannot verify source attributes from record bytes alone.
It must publish the accepted tail only after the required barrier.
The decoder does not load or store shared head/tail fields and supplies no concurrency synchronization.
The adapter must preserve the ring until all readers stop and must prevent handler reentry.
It must bound total records and elapsed work per dispatch. Repeated calls are not themselves a bounded drain.
Unread data remains explicit. A caller cannot declare a complete drain merely because its work budget expired.

This component does not establish source epochs, native credit, payment ownership, signal safety, or CLR compatibility.
The `perf-sample-ring` checks pass on Windows with MSVC and Linux with Clang.
They use synthetic ring bytes. The Linux adapter section describes live verification.

## Linux adapter

`LinuxPerfRingPlan::attributes(period)` returns the only accepted task-clock attributes:

| Field | Value |
| --- | --- |
| `type`, `config` | `PERF_TYPE_SOFTWARE`, `PERF_COUNT_SW_TASK_CLOCK` |
| `size` | `PERF_ATTR_SIZE_VER3` (96 bytes) |
| `sample_period` | `period` |
| `sample_type` | IP, TID, TIME, PERIOD, READ |
| `read_format` | `PERF_FORMAT_LOST` |
| `disabled`, `exclude_kernel`, `exclude_hv` | 1 |
| `use_clockid`, `clockid` | 1, `CLOCK_MONOTONIC` |
| `wakeup_events` | 1 |
| All other fields and reserved bytes | 0 |

`LinuxPerfRingPlan::parse(mapping, pageSize, attr, process, thread)` returns an optional plan.
It requires a little-endian host, a power-of-two page size, and a page-aligned mapping larger than one page.
`attr` must be byte-identical to `attributes(attr.sample_period)`.
`data_offset` must equal the page size, and `data_size` must equal the remaining mapping size.
`data_head` and `data_tail` must both be zero. A plan can therefore parse only before the first enable.
The data area must also satisfy the `count_and_loss` decoder plan.

`LinuxPerfRing(plan)` creates the only consumer of the mapping. It cannot be copied or moved.
The caller owns the mapping, the event descriptor, and exclusive consumer access.

`read()` acquires `data_head`, decodes at most one record, and returns `LinuxPerfRingRead`.
After an accepted sample, it issues a full memory fence and then stores the accepted tail.
It does not store a tail after a failed or empty read.
`snapshot_complete` is true only when no error exists and the accepted tail equals the acquired head.
A complete snapshot does not prove source closure. The producer can publish more records after the head read.
`read()` uses no loop, allocation, callback, or OS call.

`LinuxPerfCountLoss::parse(returned, bytes)` decodes the 16-byte `read(fd)` result for `PERF_FORMAT_LOST`.
It accepts only a return value of exactly 16. Other lengths, zero, and errors reject.

### Epoch closure contract

A source epoch is complete only when all of these conditions are true, in this order:

| Order | Condition |
| ---: | --- |
| 1 | `PERF_EVENT_IOC_DISABLE` succeeded. |
| 2 | `read()` reached `snapshot_complete` after the disable, with no error. |
| 3 | `LinuxPerfCountLoss::parse` accepted the 16-byte read from the event descriptor. |
| 4 | The parsed `lost` value is zero. |

The next epoch can start only after a complete closure. Unmapping and closing require that all consumers stopped.

Zero `PERF_RECORD_LOST` records do not prove complete capture. The kernel emits that record only after a later successful output.
The final count/loss read reports loss without a later output.

### Verified environment

`PerfRingChecks` (`perf-ring`) uses a synthetic page-aligned mapping and makes no OS calls.
`LinuxPerfRingVerify` runs the live `PerfRingWorkload` modes. See [the verification procedure](../How_Tos/verify-perf-ring.md).
Live checks pass on kernel `5.15.153.1-microsoft-standard-WSL2` with x64 and `perf_event_paranoid = 2`.
The container drops all capabilities and uses `seccomp=unconfined`.
`PERF_FORMAT_LOST` on that kernel is a Microsoft backport. Its version prefix does not prove support on other kernels.
Other kernels, architectures, and seccomp policies are not verified.
The adapter targets only Linux x64. Other memory models need their own acquire and release operations.
CMake builds `PerfRingChecks` and `PerfRingWorkload` only when `<linux/perf_event.h>` defines `PERF_FORMAT_LOST`.
Without that definition, CMake skips both targets and reports a status message. Other service checks still build.
CMake caches the header result as `COZ_HAVE_PERF_FORMAT_LOST`. After a header upgrade, use a new build directory or clear that entry.
The workload thread is both the producer CPU and the consumer. Cross-CPU head and tail visibility is not verified.
See [target-local processing](../Explanations/target-local-sample-processing.md) for the design context.
