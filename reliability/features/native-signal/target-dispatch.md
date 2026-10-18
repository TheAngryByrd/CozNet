# Target-local sample dispatch

Evidence: `artifacts/perf-ring/20261018T124448773-6d9eabd4`.

`LinuxPerfRingVerify` builds the checks and runs each `TargetDispatchWorkload` mode once after the `PerfRingWorkload` modes.
The kernel is `5.15.153.1-microsoft-standard-WSL2` on x64, with `perf_event_paranoid = 2`.
The container drops all capabilities, has no network, and uses `seccomp=unconfined`.
The `perf-ring`, `perf-sample-ring`, and `target-sample-dispatch` checks pass. All source and binary hashes remain unchanged.
Each perf sample overflow requests one `SIGRTMIN + 8` signal to the target thread through fasync.
The kernel merges overflows that occur before its deferred wakeup work runs.
Each mode file retains every dispatch, every journal sample, the epoch records, and the retirement record.

| Mode | Notifications | Journal samples | Budget stops with backlog | Closure steps | Final lost | Epoch result |
| --- | ---: | ---: | ---: | --- | --- | --- |
| `bounded` | 272 | 272 | 0 | 1, 1, 1 | 0, 0, 0 | Three complete epochs |
| `backlog` | 114 | 152 | 29 | 5 | 0 | Complete |
| `incomplete` | 309 | 93 | 72 | 0 | 198 (raw) | Terminal, `source_failed` |
| `cancel` | 144 | 144 | 0 | 12 | 0 | Complete, resume refused |

All modes report zero stale, reentered, foreign, `SIGIO` fallback, and dropped log entries.
In every mode, a second `create` on the target thread returns `EBUSY`.
Every retirement blocks the signal, disables the event, clears `O_ASYNC`, unmaps, closes, and restores the mask with no error.
The maximum handler interval in any mode is 12,680 ns.

In `bounded`, every one of the 272 dispatches reads exactly one sample and ends with `drained_snapshot`.
The median handler interval is 293 ns. The maximum is 5,620 ns.
The ring wraps. Epoch values 100036800, 200047700, and 300055600 ns increase, and each covers its last sample reading.

In `backlog`, two masked steps queue notifications while samples accumulate.
After the first mask ends, 29 one-record dispatches stop with backlog `remaining`. Later queued notifications drain it.
The second mask holds into closure. The session drains 38 queued signals without dispatch. Closure reads the backlog in 5 bounded steps.

In `incomplete`, a 300 ms masked step overflows the one-page ring. Before the mask, 20 dispatches read one sample each.
After the mask ends, 73 one-record dispatches empty the ring. 72 of them stop with backlog `remaining`.
The next 198 queued notifications find an empty ring. The kernel requests a notification for each overflow, including each lost sample.
The next successful output writes a `PERF_RECORD_LOST` record first. The decoder rejects it (error 5).
The dispatcher then stays terminal. Its 17 later notifications return `terminal` and read nothing.
All 93 accepted samples keep generation 1. Retirement reads 198 lost samples from the descriptor. Resume returns `EPROTO`.

In `cancel`, another thread cancels after 50 ms of wall time. 49 dispatches accept samples before cancellation, and 95 dispatches after it read nothing.
Closure ignores cancellation and drains the remaining 95 samples in 12 steps. The epoch completes with zero loss, and resume returns `EPROTO`.

No live mode reached the 250,000 ns elapsed budget. Elapsed-budget stops have synthetic checks only.

## Rejected trigger

Evidence: `artifacts/perf-ring/20261018T114054564-e9338966`.

The first workload used a `CLOCK_THREAD_CPUTIME_ID` timer with a 2 ms interval.
Its wall-time gap between handler starts has a median near 10 ms in every mode. Most deliveries report overrun 4.
`bounded` received 30 notifications for 300 ms of thread CPU. 28 of them stopped at the 8-record budget with backlog.
At 50% speedup, ten samples in one notification can publish 5 ms of credit. That conflicts with the 2 ms debt bound.
That run passed its declared rules. The cadence evidence caused the change to fasync notification.

## Limits

These runs do not test CLR execution, native credit, payment, or prediction accuracy.
The workload thread is both producer CPU and consumer. Cross-CPU publication is not observed.
One notification for each sample costs about one signal for each 1 ms of thread CPU. Its total overhead in an application is not measured.
Each run uses one session. Concurrent retirement of two sessions and the handler quiescence timeout are not tested.
The code keeps session memory after a timeout, but no check forces that path.
Fasync identity uses the descriptor number. A reused number after retirement can cause one extra bounded drain of another session's own ring.
Other kernels, signal queue limits, and seccomp policies are not verified.

See [the dispatch reference](../../../docsSrc/Reference/target-sample-dispatch.md), [the procedure](../../../docsSrc/How_Tos/verify-perf-ring.md#check-target-local-dispatch), and [the plan](../../../lode/plans/target-local-dispatch.md).
