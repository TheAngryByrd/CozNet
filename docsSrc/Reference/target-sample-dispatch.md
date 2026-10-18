# Target sample dispatch

`native/service/TargetSampleDispatch.h` defines the portable bounded dispatcher.
`native/service/TargetSampleSession.Linux.h` defines the Linux x64 session that drives it from a perf notification signal.
Neither component publishes credit or performs payment.

## Dispatch budget

`DispatchBudget::parse(records, elapsed_ns)` returns a budget or no value.

| Field | Accepted range |
| --- | --- |
| `records` | 1 to 4096 |
| `elapsed_ns` | 1 to 4,999,999 |

The elapsed limit is less than the 5 ms handler bound. A dispatch is one part of a handler.

## Dispatcher

`TargetSampleDispatcher<Source, Clock, Sink>` holds references to its source, clock, and sink. It cannot be copied.
All member functions are `noexcept`. The dispatcher makes no OS calls and does not allocate.

| Parameter | Required members |
| --- | --- |
| `Source` | `read()` returns `.record` and `.snapshot_complete`. `enable()` and `disable()` return 0 or an error. `finalCount()` returns an optional value with `.value` and `.lost`. |
| `Clock` | `now()` returns nanoseconds, or 0 on failure. |
| `Sink` | `available()` returns the free entry count. `accept(sample, generation, dispatch)` returns false on failure. |

`SampleJournal` is a fixed-capacity sink over caller storage. Each entry keeps the sample, its epoch generation, and its dispatch number.

### Dispatch loop

`notify({generation, sequence}, budget)` runs one dispatch. Before each read, it checks these conditions in order:

1. The clock returns a nonzero value that does not decrease.
2. The dispatcher is not cancelled.
3. The accepted count is less than the record budget.
4. The elapsed time is less than the elapsed budget.
5. The sink has a free entry.

A read publishes the source tail. The checks therefore occur before the read, and a sample that the sink cannot hold stays in the source.
A read that returns no sample and no completed snapshot is a source failure.

### Outcomes

| Outcome | Meaning | Backlog |
| --- | --- | --- |
| `drained_snapshot` | The last read reached the head that it acquired. | `none` |
| `records_exhausted` | The record budget stopped the dispatch. | `remaining`, or `unknown` before any read |
| `time_exhausted` | The elapsed budget stopped the dispatch. | `remaining`, or `unknown` before any read |
| `sink_full` | The sink had no free entry. | `remaining`, or `unknown` before any read |
| `cancelled` | Cancellation stopped the dispatch. | `remaining`, or `unknown` before any read |
| `source_failed` | The source failed, or a closure check failed. | `unknown` |
| `sink_failed` | The sink rejected a read sample. | `unknown` |
| `clock_failed` | The clock returned 0 or decreased. | `unknown` |
| `reentered` | Another dispatcher call was active. | Not read |
| `stale_notification` | The generation did not match, or the sequence did not increase. | Not read |
| `not_open` | The epoch was not open, or not closing for closure calls. | Not read |
| `terminal` | An earlier failure ended the dispatcher. | Not read |

`drained_snapshot` does not prove source closure. The producer can publish more records after the head read.
Source, sink, and clock failures are terminal. Later calls return `terminal` and accept nothing.
`reentered()` and `stale()` count rejected calls.

### Epochs

| Call | Required phase | Result phase |
| --- | --- | --- |
| `start()` | `unstarted` | `open` with generation 1, or `terminal` if enable fails |
| `beginClose()` | `open` | `closing`, or `terminal` if disable fails |
| `continueClose(budget)` | `closing` | `closing` with backlog, `closed_complete`, or `terminal` |
| `resume()` | `closed_complete` and not cancelled | `open` with the next generation, or `terminal` if enable fails |

`continueClose` drains with the budget and ignores cancellation. After a completed snapshot, it calls `finalCount()`.
The epoch completes only when the count read succeeds, `lost` is 0, and `value` is not less than the last sample's cumulative CPU.
Otherwise the epoch record keeps `complete = false` and a failure reason: `count_read_failed`, `lost_samples`, or `count_regressed`.

`epoch()` returns the current `TargetEpochRecord`: generation, accepted count, last cumulative CPU, final value, final loss, closure steps, completion, and failure.
A failed epoch keeps all accepted samples in the sink under its own generation. No later epoch can start.

`cancel()` is the only member function that another thread can call. Cancellation is permanent.
The calling thread must return from `cancel()` before the owner releases the session handle.

## Linux session

`LinuxTargetSampleSession` owns one task-clock event for the calling thread. It requires headers that define `PERF_FORMAT_LOST`.

| Resource | Owner action |
| --- | --- |
| Perf event | Opened disabled with `LinuxPerfRingPlan::attributes(period)` |
| Mapping | One metadata page and a power-of-two count of data pages |
| Notification | `F_SETOWN_EX` with `F_OWNER_TID`, `F_SETSIG` with `SIGRTMIN + 8`, and `O_ASYNC` |
| Dispatcher and journal | Created before notification starts. The caller supplies journal and log storage. |
| Registry slot | One of 64 process-wide slots. The handler compares slots and does not dereference signal values. |

`install()` requires the default disposition for `SIGRTMIN + 8` and `SIGIO`. It installs the notification handler and a `SIGIO` counter.
A `SIGIO` signal means that the kernel could not queue a notification. The samples stay in the ring.
`install()` and `restore()` share one process-wide owner across all sink types. `install()` returns `EBUSY` while another sink type owns the signal. `restore()` returns `EPERM` from a sink type that does not own it.
`restore()` fails with `EBUSY` while any session of any sink type is registered, or while the calling thread has a pending `SIGIO` or notification signal.
It cannot see signals pending on other threads. Every target thread must retire successfully before `restore()`.

The handler accepts a signal only with `si_code = POLL_IN`, the session descriptor in `si_fd`, and the session thread.
Other signals increment `foreignSignals()`. The handler saves and restores `errno`.

All session functions except `cancel()` must run on the thread that called `create()`.

| Function | Sequence |
| --- | --- |
| `create(config, journal, log, session)` | Block the signal, open, map, parse, register, and set the notification owner. A second session on one thread returns `EBUSY` before any side effect. Failure retires the partial session. |
| `start()` | Enable the event, publish generation 1, and restore the signal mask. |
| `closeEpoch(budget, maxSteps)` | Block the signal, disable if the epoch is open, drain queued signals, and call `continueClose` up to `maxSteps` times. Only a record or elapsed budget stop starts another step. A `sink_full` result ends the call. A later call continues a closing epoch. |
| `resume()` | Enable the next generation and restore the signal mask. |
| `retire()` | Block, disable, clear `O_ASYNC`, drain queued signals, unregister, and wait up to 100 ms for active handlers. Read the raw count if the epoch did not close, then unmap, close, and restore the mask. |

`retire()` returns `TargetRetirement`. The mask stays blocked if an earlier step fails.
`create` returns a `LinuxTargetSampleSession::Handle`. Its deleter retires the session and frees it only when `quiescent` is true.
After a handler quiescence timeout, the session memory stays allocated because another thread's handler can still hold its address.
`lifecycleError()` reports a failed block or signal drain during `closeEpoch`. The value stays set after a later call succeeds.
A `closeEpoch` result of `source_failed` with a nonzero `lifecycleError()` is a session failure, not a dispatcher failure.

The handler appends one `TargetDispatchLogEntry` for each accepted notification. A full log counts `dropped()` entries.

## Verified environment

`TargetSampleDispatchChecks` (`target-sample-dispatch`) runs on Windows and Linux.
`LinuxPerfRingVerify` runs the live `TargetDispatchWorkload` modes on kernel `5.15.153.1-microsoft-standard-WSL2`.
See [the procedure](../How_Tos/verify-perf-ring.md#check-target-local-dispatch) and [the decoder reference](native-perf-decoder.md).
These checks do not test CLR execution, credit publication, or payment.
