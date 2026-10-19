# Target-local forced-GC transitions

Evidence: `artifacts/target-local-transitions/20261019T082925705-0e9bf3e5`.

`TargetLocalTransitionsVerify` ran `profile-gc 50 6000 3000 5` ten times in the Linux x64 container.
The run records kernel `5.15.153.1-microsoft-standard-WSL2`, x64, with 4 CPUs. Earlier transition runs did not record the host.
While at least one profiled thread is in its work phase, the main thread forces a blocking gen-2 GC every 5 ms.
The criteria are in [the transition plan](../../../lode/plans/target-local-transitions.md). The runner hashes that file, and all source hashes stay unchanged.
The sample runs through the checked library entry point `TargetLocalProfiler.runBound` with a `CodeBinding`. `native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current check passes.** Earlier failed checks stay part of the evidence.

| Run | Passed | Ledger failure | Max observed debt (ns) | Forced GCs in work window | Max handler (ns) | Wall minus counted CPU (ms) | Epochs complete, zero loss |
| ---: | --- | --- | ---: | ---: | ---: | ---: | --- |
| 1 | Yes | None | 949,984 | 20 | 1,008,645 | 8.8 | Yes |
| 2 | Yes | None | 999,996 | 19 | 1,950,995 | 7.8 | Yes |
| 3 | Yes | None | 949,991 | 20 | 1,012,287 | 7.2 | Yes |
| 4 | Yes | None | 999,959 | 20 | 1,503,664 | 7.6 | Yes |
| 5 | Yes | None | 999,987 | 19 | 1,504,681 | 7.0 | Yes |
| 6 | Yes | None | 1,000,000 | 20 | 1,513,079 | 10.5 | Yes |
| 7 | Yes | None | 999,980 | 19 | 1,504,567 | 6.2 | Yes |
| 8 | Yes | None | 949,998 | 19 | 1,505,448 | 5.8 | Yes |
| 9 | Yes | None | 899,950 | 19 | 1,008,612 | 5.9 | Yes |
| 10 | Yes | None | 999,960 | 20 | 1,504,558 | 7.3 | Yes |

No run crashed or hung. Every run closed both epochs with zero loss, retired both sessions, and restored the handlers.
The `cleanup` check passes. A profile whose monitor throws raises `AggregateException`, and a second profile in the same process then passes.
The `barrier-abort` check passes. A worker that throws releases its waiting peer, the run fails within 10 s, and a second profile then passes.
Every handler interval stays below 5 ms, and no run records a ledger failure. The longest is 1,950,995 ns.
Wall minus counted CPU is the largest per-thread difference. Counted CPU stops at disable and wall time includes the closing payment, so it is coarse.

## GC windows

The GC window records are complete. Each forced `GC.Collect` call is bracketed by `Stopwatch` reads on the monitor thread.
The pause total from `GC.GetTotalPauseDuration` also counts collections that the sample does not force.
A debt failure lists the window time inside the interval from the sample before the failing sample to the failure. For a failure at the first sample of a worker, the interval starts at the native start of that worker. An incomplete record reports no windows.

| Run | Complete | Windows | Window total | Pause total | Largest event gap | Debt failures |
| ---: | --- | ---: | ---: | ---: | --- | --- |
| 1 | Yes | 20 | 6.7876 ms | 5.6220 ms | worker 0: 4.7193 ms with 0.9290 ms in windows | none |
| 2 | Yes | 19 | 11.3705 ms | 7.7750 ms | worker 0: 2.2105 ms with 1.2314 ms in windows | none |
| 3 | Yes | 20 | 9.0024 ms | 7.3990 ms | worker 0: 2.2506 ms with 1.3383 ms in windows | none |
| 4 | Yes | 20 | 8.9120 ms | 7.6030 ms | worker 0: 2.1064 ms with 1.1669 ms in windows | none |
| 5 | Yes | 19 | 8.6697 ms | 7.2280 ms | worker 0: 1.7845 ms with 1.4890 ms in windows | none |
| 6 | Yes | 20 | 11.4967 ms | 10.2260 ms | worker 0: 2.2628 ms with 1.1836 ms in windows | none |
| 7 | Yes | 19 | 7.6447 ms | 6.2510 ms | worker 0: 1.7927 ms with 0.8505 ms in windows | none |
| 8 | Yes | 19 | 6.8713 ms | 5.6860 ms | worker 0: 1.8503 ms with 0.8985 ms in windows | none |
| 9 | Yes | 19 | 6.7951 ms | 5.5470 ms | worker 0: 1.7519 ms with 0.7693 ms in windows | none |
| 10 | Yes | 20 | 8.8466 ms | 7.4780 ms | worker 0: 1.9205 ms with 1.4503 ms in windows | none |

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261018T151031617-addf680d` | First check | Fail. 1 of 10 runs broke the debt bound. Its collection count was flawed. |
| `20261018T153537759-847ee42d` | Count only collections in the work window | Pass, 10 of 10 |
| `20261018T155026644-209a9e05` | Profiler moved into the library | Fail. Every run stopped on a perf-map parser defect before profiling. |
| `20261018T155303110-d6c77c03` | Parser accepts the `0x` prefix | Pass, 10 of 10 |
| `20261018T161924408-457ccd0e` | Cleanup on failure, native layout check, and payment address check | Pass, 10 of 10 |
| `20261018T162853008-0529c4ad` | Join retry, construction before open, and cleanup check | Pass, 10 of 10 |
| `20261018T182834143-78a15e0b` | Wait protocol in the ledger and the library | Fail. Run 4 broke the debt bound while the peer accepted no sample for six selected samples. |
| `20261018T183151879-34460604` | Per-thread CPU and wall time diagnostics | Pass, 10 of 10 |
| `20261018T190118893-b2109c5d` | Barrier abort, post race, and token bound | Fail. Run 8: 2 ms debt bound. |
| `20261018T191244704-9849fce2` | Barrier errors cancel the run | Pass, 10 of 10 |
| `20261018T192454148-24d37dd2` | Run-queue delay diagnostic | Pass, 10 of 10 |
| `20261018T202431027-b539e1a4` | Code binding, tiered rejection, and shutdown checks | Pass, 10 of 10 |
| `20261018T203245961-56927b66` | Run-queue reads outside the timed window | Pass, 10 of 10 |
| `20261018T210459943-dfa8e472` | Review fixes: perf-map setting check, checked-run seam, shutdown work-state marker, and wording | Pass, 10 of 10 |
| `20261018T213950841-bc5dbc42` | Per-wait run-queue delay, pread reader, and LF-normalized source hashes, before review fixes | Fail. Run 4: 2 ms debt bound under 19 forced GCs. |
| `20261018T221219867-6603996f` | Review fixes: wait copy after the finish time, scalar parser, debt-exceeded and dependency-wake pairing | Pass, 10 of 10 |
| `20261018T233751688-5acdb8cc` | Start run-queue delay diagnostic | Pass, 10 of 10 |
| `20261019T001542478-b4d2cdb6` | Start trace, with on-CPU time from schedstat field 1, before the revision | Fail. Run 8: 2 ms debt bound at 39 ms, after a 6.5 ms peer gap with no event. The peer has 5.3 ms of residual run-queue delay. |
| `20261019T002340083-c152f4ca` | Start trace with the thread CPU clock | Pass, 10 of 10 |
| `20261019T004724440-fd9cd00c` | Thread state set before the native start, criterion 27 for barrier runs | Pass, 10 of 10 |
| `20261019T011520304-41a7c250` | Forced-GC windows | Pass, 10 of 10 |
| `20261019T014043978-f7a0ddbc` | ReJIT detection check | Fail. Run 7: 2 ms debt bound. GC windows cover 0.22 ms of the 5.75 ms failure interval. The ReJIT check passes. |
| `20261019T020010673-c0788652` | Linux backend file split and lifecycle plan update | Pass, 10 of 10 |
| `20261019T024210440-65ed47ee` | Causal profile driver | Pass, 10 of 10 |
| `20261019T032230942-d027dbb5` | Throughput progress points | Fail. Run 10: 2 ms debt bound. One GC window covers 1.16 ms of the 5.77 ms failure interval. |
| `20261019T040604415-1a034540` | Blocking queue and pipeline profile | Fail. Run 9: 2 ms debt bound. Two GC windows cover 0.91 ms of the 6.50 ms failure interval. |
| `20261019T043835718-5c15e486` | Per-thread experiment diagnostics | Pass, 10 of 10 |
| `20261019T051202514-ddf03668` | Profile warm-up experiment | Fail. Run 1: 2 ms debt bound. One GC window covers 1.31 ms of the 5.26 ms failure interval. |
| `20261019T060721421-9e406e92` | Rotated experiment order and warm-up gate | Pass, 10 of 10 |
| `20261019T063102813-1448d8f5` | TargetLock and lock profile | Pass, 10 of 10 |
| `20261019T070731753-86c6ac1e` | Lock handoff counts | Pass, 10 of 10 |
| `20261019T074936096-7103de7f` | Spin before a lock wait | Pass, 10 of 10 |
| `20261019T082925705-0e9bf3e5` | TargetSemaphore and semaphore profile | Pass, 10 of 10 |

## Earlier failed check

The first check counted collections from thread start to join. At least 5 ms passes between its forced collections.
Its 42 to 66 collections therefore needed 210 to 330 ms of thread lifetime, while its timed work lasted about 110 ms.
Most of its collections fell outside profiling, so that check did not measure what it claimed.

That check also failed. Nine of ten runs passed. Run 4 recorded `debt_exceeded`.
In sequences 71 through 80 of run 4, the selected thread accepts five selected samples and publishes 2.5 ms of credit.
The journal holds no peer sample in that interval. Sequence 82 records the failure at 2,499,985 ns of debt.
The ledger then pays the debt in bounded children. It forgives nothing.
The journal has no scheduling or GC suspension times, so it cannot show why the peer accepted no sample.
One hypothesis is that the host or the GC deschedules one thread. Profile results now record thread CPU and wall time to test it.

## Consequence

The passing current-design checks hold 230 runs with no failure. Eight current-design checks each fail 1 of 10 runs by the 2 ms debt bound, the same bound as the first failure.
A thread can go without samples long enough to break the 2 ms debt bound. The cause is unknown.
GC-heavy workloads stay outside the supported model. The debt bound stays unchanged.

## Check limits

The current runner requires exactly 10 runs. The first run used an earlier runner revision with 10 runs.
A forced collection starts only while a worker reports that it is working. The count does not show how long each collection overlaps the work.
The checks do not measure prediction accuracy under GC.

## Lifecycle checks

- `bindingTieredPassed`: pass
- `rejitPassed`: pass
- `bindingDefaultPassed`: pass
- `shutdownPassed`: pass

`bindingTiered` runs with tiering on and requires both a configuration error and a rejected capture. `bindingDefault` requires a capture that still verifies after 100 ms of work.
`shutdown` requires an exit through `Environment.Exit` while a worker is still working. In this run the exit code is 0. The check does not show that a handler was inside a payment spin at the exit.
`rejit` loads a test CLR profiler that requests a ReJIT of `Work.selected` during the run. It requires the ReJIT to finish and the run to fail with `BindingError`. See [the ReJIT plan](../../../lode/plans/target-local-rejit.md).
The checks do not cover thread abort. See [the lifecycle plan](../../../lode/plans/target-local-lifecycle.md).

See [the support boundary](../../../docsSrc/Explanations/target-local-prediction.md) and [the prediction qualification](prediction.md).
