# Target-local forced-GC transitions

Evidence: `artifacts/target-local-transitions/20261018T162853008-0529c4ad`.

`TargetLocalTransitionsVerify` ran `profile-gc 50 6000 3000 5` ten times in the Linux x64 container.
The run records kernel `5.15.153.1-microsoft-standard-WSL2`, x64, with 4 CPUs. Earlier transition runs did not record the host.
While at least one profiled thread is in its work phase, the main thread forces a blocking gen-2 GC every 5 ms.
The criteria are in [the transition plan](../../../lode/plans/target-local-transitions.md). The runner hashes that file, and all source hashes stay unchanged.
The sample runs through the library entry point `TargetLocalProfiler.run`. `native/WriteTargetLocalEvidence.py` writes this page.

**Result: the current check passes, and an earlier check failed.** The failure stays part of the evidence.

| Run | Passed | Ledger failure | Max observed debt (ns) | Forced GCs in work window | Max handler (ns) | Epochs complete, zero loss |
| ---: | --- | --- | ---: | ---: | ---: | --- |
| 1 | Yes | None | 999,954 | 19 | 1,523,986 | Yes |
| 2 | Yes | None | 999,974 | 19 | 1,010,446 | Yes |
| 3 | Yes | None | 999,971 | 19 | 1,005,336 | Yes |
| 4 | Yes | None | 899,949 | 19 | 1,006,885 | Yes |
| 5 | Yes | None | 999,971 | 20 | 1,504,494 | Yes |
| 6 | Yes | None | 999,987 | 19 | 1,503,750 | Yes |
| 7 | Yes | None | 999,994 | 20 | 1,504,992 | Yes |
| 8 | Yes | None | 999,970 | 20 | 2,005,951 | Yes |
| 9 | Yes | None | 999,987 | 20 | 1,506,720 | Yes |
| 10 | Yes | None | 999,997 | 19 | 1,504,630 | Yes |

No run crashed or hung. Every run closed both epochs with zero loss, retired both sessions, and restored the handlers.
The `cleanup` check passes. A profile whose monitor throws raises `AggregateException`, and a second profile in the same process then passes.
Every handler interval stays below 5 ms, and no run records a ledger failure.

## Run history

| Run | Change before the run | Result |
| --- | --- | --- |
| `20261018T151031617-addf680d` | First check | Fail. 1 of 10 runs broke the debt bound. Its collection count was flawed. |
| `20261018T153537759-847ee42d` | Count only collections in the work window | Pass, 10 of 10 |
| `20261018T155026644-209a9e05` | Profiler moved into the library | Fail. Every run stopped on a perf-map parser defect before profiling. |
| `20261018T155303110-d6c77c03` | Parser accepts the `0x` prefix | Pass, 10 of 10 |
| `20261018T161924408-457ccd0e` | Cleanup on failure, native layout check, and payment address check | Pass, 10 of 10 |
| `20261018T162853008-0529c4ad` | Join retry, construction before open, and transition host record | Pass, 10 of 10 |

## Earlier failed check

The first check counted collections from thread start to join. At least 5 ms passes between its forced collections.
Its 42 to 66 collections therefore needed 210 to 330 ms of thread lifetime, while its timed work lasted about 110 ms.
Most of its collections fell outside profiling, so that check did not measure what it claimed.

That check also failed. Nine of ten runs passed. Run 4 recorded `debt_exceeded`.
In sequences 71 through 80 of run 4, the selected thread accepts five selected samples and publishes 2.5 ms of credit.
The journal holds no peer sample in that interval. Sequence 82 records the failure at 2,499,985 ns of debt.
The ledger then pays the debt in bounded children. It forgives nothing.
The journal has no scheduling or GC suspension times, so it cannot show why the peer accepted no sample.
One hypothesis is that the GC suspended and resumed the two threads at different times. No check can test it yet.

## Consequence

The current-design checks show no failure in 40 runs, with 19 to 20 forced collections during profiling in the latest run.
The earlier failure shows that a thread can go without samples long enough to break the 2 ms debt bound. Its cause is unknown.
GC-heavy workloads stay outside the supported model. The debt bound stays unchanged.

## Check limits

The current runner requires exactly 10 runs. The first run used an earlier runner revision with 10 runs.
A forced collection starts only while a worker reports that it is working. The count does not show how long each collection overlaps the work.
The checks do not measure prediction accuracy under GC. They do not cover JIT, tiering, ReJIT, thread abort, or process exit.

See [the support boundary](../../../docsSrc/Explanations/target-local-prediction.md) and [the prediction qualification](prediction.md).
