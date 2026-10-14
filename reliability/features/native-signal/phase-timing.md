# Native signal phase timing

Native timing places most of one long sampled acknowledgment before the handler's first clock reading.
Other requests show long native waits or delayed observation after the measured handler interval.
These results do not establish stable capacity, general CLR safety, or prediction accuracy.

## Clock boundaries

`coz_signal_read_timed` returns the original 48-byte result and a separate 40-byte timing value.
The original `coz_signal_read` export keeps its ABI and ownership rules.
The native timing values use Linux `CLOCK_MONOTONIC`, expressed in nanoseconds.
The handler retains its existing start and finish readings. It adds stores but no clock calls.
The controller adds readings before queue submission, after the queue syscall, and when it observes the published result.

| Duration | Difference | Included work |
| --- | --- | --- |
| Before handler | First handler reading minus request start | Queue submission, scheduling, delivery, and handler entry checks |
| Measured handler | Last handler reading minus first handler reading | Eligibility check and any native wait |
| After handler | Native observation minus last handler reading | Result publication, scheduling, and controller polling |
| Queue call | Queue return minus request start | Submission and any controller delay during that interval |

The queue call can overlap handler execution. Do not add it to the other three durations.
The last handler reading precedes result publication and signal return.
The timing does not establish the complete handler residence or the exact instant of signal delivery.
The process `Stopwatch` and perf timestamps retain their separate clock contracts.

`SignalPhaseTiming.Parse` rejects zero origins, invalid ordering, and disagreement with the measured handler duration.
It permits the handler to finish before the queue syscall returns.
The backend preserves all raw result and timing fields in an exception if parsing fails.
Other backends can omit optional phase timing until they supply equivalent observations.

## Native and managed checks

The [signal archive](data/phase-timing/run-20261014T013600477-9255e038.zip) retains ten process outcomes.
The timing checks and three native contract cases pass. Five of six managed cases pass.
The delay case without forced GC fails the unchanged 5 ms measured-handler limit.
Request 2 records 12.338697 ms of wait and a 12.339968 ms handler interval.
Its native time before the first handler reading is 0.016354 ms. Time after the last reading is 0.204227 ms.
This failure remains valid. Passing GC cases do not erase it.

The [transition archive](data/phase-timing/run-20261014T013702842-1e3d24ff.zip) retains fourteen passing process outcomes.
These include five native cases, the managed target-lifetime checks, and eight managed transition cases.
The cases exercise JIT, blocked waits, managed/native calls, forced GC, and shutdown.
The compiled handler passes the existing check for direct syscalls without calls through the PLT or TLS access.

| Requirement | Executed evidence |
| --- | --- |
| Elapsed phase values | `SequentialPhases` |
| Handler and queue-call overlap | `HandlerCanFinishBeforeQueueReturns` |
| Equal clock observations | `EqualBoundariesAreValid` |
| Large absolute clocks | `LargeClockValuesKeepSmallDurations` |
| Invalid ordering and duration | `RejectMalformedTimings`, seven rejected cases |
| Native result layout and ownership | `SignalContractChecks lifetime`, timed and legacy reads |
| Portable parser execution | `--verify-signal-timing` passes on Windows and Linux |
| Runtime transitions | `LinuxSignalTransitionsVerify` passes |

## Sampled result

The [sampled archive](data/phase-timing/run-20261014T013810693-6445bed0.zip) retains the three warm blocks.
All six protocol cases and all three zero-percent capacity cases pass.
The second 50-percent case fails capacity. The target therefore fails overall.
Limits remain 50 µs requested wait, 5 ms handler interval, 100 ms acknowledgment, and 2 ms observed debt.
The two-percentage-point prediction gate remains unchanged and untested here.

| 50-percent block | Credit (ms) | Peak selected debt (ms) | Peak peer debt (ms) | Capacity |
| --- | ---: | ---: | ---: | --- |
| 1 | 905.5 | 0.422583 | 0.812898 | Pass |
| 2 | 906.5 | 2.367731 | 4.620465 | Fail |
| 3 | 904 | 0.258294 | 1.122047 | Pass |

In block 2, peer request 8012 is pending at the peer peak.
It eventually reports a deferred interruption outside the selected method and performs no native wait.

| Request 8012 observation | Duration |
| --- | ---: |
| Process acknowledgment | 8.8139 ms |
| Native time before first handler reading | 8.773081 ms |
| Measured handler interval | 0.000191 ms |
| Native time after last handler reading | 0.038099 ms |
| Queue-call interval, which overlaps the other phases | 0.002291 ms |

Most of this request's native elapsed time precedes the first handler reading.
Delayed observation after the measured interval does not account for most of that duration.
Scheduling, delivery, and handler entry checks remain unresolved within the first phase.
The selected peak follows peer request 8990, which records 3.069057 ms of native wait.
The effect of a different wait method remains untested.

Block 1 also contains a delayed observation after a short handler interval.
Request 6892 records 0.118823 ms in the handler and 1.015336 ms after its last reading.
The data therefore contain delays in more than one phase.
Inspect target scheduling and CLR suspension before attributing the pre-handler gap to a specific cause.

## Verification and limits

The signal archive verifies ten source hashes and three binary hashes.
The transition archive verifies nineteen source/binary hashes. The sampled archive verifies twenty-one.
The timing checks reconcile 1,294 signal, 7,282 transition, and 28,645 sampled results.
The sampled ledger also reconciles credits and waits across twelve phases.
Every archive member matches its original file by SHA-256.

All measurements use the existing Windows host and Linux x64 containers.
Additional controller clock calls can affect scheduling and overhead.
Neither the two passing sampled cases nor these diagnostics establish general accuracy or cross-platform runtime support.
Keep the earlier [service timing records](service-timing.md) and [capacity failures](sampled.md).

See [native contracts](../../../lode/profiling/signal-intervention.md) and [sampled controller contracts](../../../lode/profiling/sampled-signals.md).
