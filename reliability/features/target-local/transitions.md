# Target-local forced-GC transitions

Evidence: `artifacts/target-local-transitions/20261019T020010673-c0788652`.

`TargetLocalTransitionsVerify` ran `profile-gc 50 6000 3000 5` ten times in the Linux x64 container.
The run records kernel `5.15.153.1-microsoft-standard-WSL2`, x64, with 4 CPUs. Earlier transition runs did not record the host.
While at least one profiled thread is in its work phase, the main thread forces a blocking gen-2 GC every 5 ms.
The criteria are in [the transition plan](../../../lode/plans/target-local-transitions.md). The runner hashes that file, and all source hashes stay unchanged.
The sample runs through the checked library entry point `TargetLocalProfiler.runBound` with a `CodeBinding`. `native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current check passes.** Earlier failed checks stay part of the evidence.

| Run | Passed | Ledger failure | Max observed debt (ns) | Forced GCs in work window | Max handler (ns) | Wall minus counted CPU (ms) | Epochs complete, zero loss |
| ---: | --- | --- | ---: | ---: | ---: | ---: | --- |
| 1 | Yes | None | 949,976 | 20 | 1,007,357 | 7.6 | Yes |
| 2 | Yes | None | 999,999 | 19 | 1,504,412 | 6.3 | Yes |
| 3 | Yes | None | 999,973 | 19 | 1,503,790 | 6.5 | Yes |
| 4 | Yes | None | 999,985 | 19 | 1,504,122 | 5.9 | Yes |
| 5 | Yes | None | 999,996 | 19 | 1,504,269 | 6.5 | Yes |
| 6 | Yes | None | 949,941 | 20 | 1,504,208 | 7.1 | Yes |
| 7 | Yes | None | 949,941 | 19 | 1,506,522 | 6.3 | Yes |
| 8 | Yes | None | 999,969 | 20 | 1,504,395 | 6.4 | Yes |
| 9 | Yes | None | 949,982 | 19 | 1,494,845 | 7.2 | Yes |
| 10 | Yes | None | 999,962 | 19 | 1,504,225 | 6.0 | Yes |

No run crashed or hung. Every run closed both epochs with zero loss, retired both sessions, and restored the handlers.
The `cleanup` check passes. A profile whose monitor throws raises `AggregateException`, and a second profile in the same process then passes.
The `barrier-abort` check passes. A worker that throws releases its waiting peer, the run fails within 10 s, and a second profile then passes.
Every handler interval stays below 5 ms, and no run records a ledger failure. The longest is 1,506,522 ns.
Wall minus counted CPU is the largest per-thread difference. Counted CPU stops at disable and wall time includes the closing payment, so it is coarse.

## GC windows

The GC window records are complete. Each forced `GC.Collect` call is bracketed by `Stopwatch` reads on the monitor thread.
The pause total from `GC.GetTotalPauseDuration` also counts collections that the sample does not force.
A debt failure lists the window time inside the interval from the sample before the failing sample to the failure. For a failure at the first sample of a worker, the interval starts at the native start of that worker. An incomplete record reports no windows.

| Run | Complete | Windows | Window total | Pause total | Largest event gap | Debt failures |
| ---: | --- | ---: | ---: | ---: | --- | --- |
| 1 | Yes | 20 | 8.8860 ms | 7.6470 ms | worker 0: 2.0734 ms with 1.1268 ms in windows | none |
| 2 | Yes | 19 | 7.4748 ms | 6.2470 ms | worker 0: 2.2622 ms with 0.2184 ms in windows | none |
| 3 | Yes | 19 | 7.6994 ms | 6.4730 ms | worker 0: 1.9225 ms with 0.9662 ms in windows | none |
| 4 | Yes | 19 | 7.1754 ms | 5.9080 ms | worker 0: 1.8077 ms with 0.8056 ms in windows | none |
| 5 | Yes | 19 | 8.1630 ms | 6.9850 ms | worker 0: 2.4745 ms with 0.4874 ms in windows | none |
| 6 | Yes | 20 | 8.5365 ms | 7.1540 ms | worker 0: 2.2004 ms with 1.2303 ms in windows | none |
| 7 | Yes | 19 | 7.8460 ms | 6.3940 ms | worker 0: 2.1986 ms with 0.2532 ms in windows | none |
| 8 | Yes | 20 | 7.6785 ms | 6.2210 ms | worker 0: 2.1288 ms with 0.1929 ms in windows | none |
| 9 | Yes | 19 | 8.5041 ms | 7.3080 ms | worker 0: 2.5693 ms with 1.5985 ms in windows | none |
| 10 | Yes | 19 | 7.3692 ms | 6.0450 ms | worker 0: 2.1405 ms with 1.1887 ms in windows | none |

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

The passing current-design checks hold 160 runs with no failure. Five current-design checks each fail 1 of 10 runs by the 2 ms debt bound, the same bound as the first failure.
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
