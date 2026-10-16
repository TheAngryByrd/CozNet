# Native perf sample decoder

`native/service/PerfSampleRing.h` defines a portable C++20 decoder for the existing Linux task-clock record layout.
It is not connected to a live source or payment backend.

## Plan

`PerfSampleRingPlan::parse(bytes, process, thread, period)` returns an optional parsed plan.
The ring must contain at least 64 bytes and have a power-of-two size.
Process identity, thread identity, and nominal period must be positive.
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
`acceptedTail()` advances by 48 bytes only after all sample checks pass.
Failure preserves the previously accepted prefix. Corrected source bytes cannot repair a terminal reader.

## Supported record layout

The decoder reads little-endian fields and permits physical ring wrap at any byte position.
This wire format is independent of the decoder host. Live perf records must come from a little-endian producer.
Big-endian perf producers need a separate adapter or decoder.
The supported `PERF_RECORD_SAMPLE` layout is:

| Offset | Width | Field |
| ---: | ---: | --- |
| 0 | 4 | Record type, `9` |
| 4 | 2 | Miscellaneous flags, user CPU mode required |
| 6 | 2 | Record length, `48` |
| 8 | 8 | Instruction pointer |
| 16 | 4 | Process identity |
| 20 | 4 | Thread identity |
| 24 | 8 | Source timestamp |
| 32 | 8 | Nominal sample period |
| 40 | 8 | Cumulative CPU reading |

Instruction pointers and timestamps must be positive. Process, thread, and period must match the parsed plan.
Timestamps and cumulative CPU readings cannot decrease. Equal readings remain valid because byte positions distinguish records.
Producer heads cannot decrease or exceed the ring capacity relative to the accepted tail.
Logical cursor rollover rejects. The reader does not treat rollover as a new stream.
Malformed lengths, loss records, throttle records, and unknown record types terminate acceptance.

## Platform obligations

The adapter must acquire the producer head according to the perf memory-order contract before decoding.
It must bind the source configuration to `PERF_SAMPLE_IP | PERF_SAMPLE_TID | PERF_SAMPLE_TIME | PERF_SAMPLE_PERIOD | PERF_SAMPLE_READ` with `read_format = 0`.
The decoder cannot verify source attributes from record bytes alone.
It must publish the accepted tail only after the required barrier.
The decoder does not load or store shared head/tail fields and supplies no concurrency synchronization.
The adapter must preserve the ring until all readers stop and must prevent handler reentry.
It must bound total records and elapsed work per dispatch. Repeated calls are not themselves a bounded drain.
Unread data remains explicit. A caller cannot declare a complete drain merely because its work budget expired.

This component does not establish source epochs, native credit, payment ownership, signal safety, or CLR compatibility.
The `perf-sample-ring` checks pass on Windows with MSVC and Linux with Clang.
They use synthetic ring bytes. They do not verify live kernel publication or concurrent ring access.
See [target-local processing](../Explanations/target-local-sample-processing.md) for the design context.
