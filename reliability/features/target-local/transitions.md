# Target-local forced-GC transitions

Evidence: `artifacts/target-local-transitions/20261018T153537759-847ee42d`.

`TargetLocalTransitionsVerify` ran `profile-gc 50 6000 3000 5` ten times in the Linux x64 container.
This runner does not record kernel identity or CPU count. The prediction runs on the same host record kernel `5.15.153.1-microsoft-standard-WSL2` and 4 CPUs.
While at least one profiled thread is in its work phase, the main thread forces a blocking gen-2 GC every 5 ms.
The criteria are in [the transition plan](../../../lode/plans/target-local-transitions.md), revised before this run. The runner hashes that file, and all source hashes stay unchanged.

**Result: the current check passes, and an earlier check failed.** The failure stays part of the evidence.

| Run | Passed | Ledger failure | Max observed debt (ns) | Forced GCs in work window | Max handler (ns) | Epochs complete, zero loss |
| ---: | --- | --- | ---: | ---: | ---: | --- |
| 1 | Yes | None | 999,982 | 20 | 1,504,346 | Yes |
| 2 | Yes | None | 1,000,000 | 19 | 1,504,599 | Yes |
| 3 | Yes | None | 899,919 | 20 | 1,007,556 | Yes |
| 4 | Yes | None | 949,970 | 20 | 1,004,172 | Yes |
| 5 | Yes | None | 1,499,966 | 20 | 1,504,459 | Yes |
| 6 | Yes | None | 1,000,000 | 19 | 1,504,076 | Yes |
| 7 | Yes | None | 949,991 | 19 | 1,007,731 | Yes |
| 8 | Yes | None | 999,994 | 20 | 1,505,023 | Yes |
| 9 | Yes | None | 799,897 | 20 | 1,006,986 | Yes |
| 10 | Yes | None | 949,939 | 19 | 1,006,046 | Yes |

No run crashed or hung. Every run closed both epochs with zero loss, retired both sessions, and restored the handlers.
Every handler interval stays below 5 ms, and no run records a ledger failure.

## Earlier failed check

Evidence: `artifacts/target-local-transitions/20261018T151031617-addf680d`.

The first check counted collections from thread start to join. At least 5 ms passes between its forced collections.
Its 42 to 66 collections therefore needed 210 to 330 ms of thread lifetime, while its timed work lasted about 110 ms.
Most of its collections fell outside profiling, so that check did not measure what it claimed. The criteria changed before the current run.

That check also failed. Nine of ten runs passed. Run 4 recorded `debt_exceeded`.
In sequences 71 through 80 of run 4, the selected thread accepts five selected samples and publishes 2.5 ms of credit.
The journal holds no peer sample in that interval. The peer's next sample, sequence 81, observes 2,499,985 ns of debt.
The ledger records the sticky failure and then pays the debt in bounded children. It forgives nothing.
The journal has no scheduling or GC suspension times, so it cannot show why the peer accepted no sample.
One hypothesis is that the GC suspended and resumed the two threads at different times. Neither check can test it.

## Consequence

The current check shows no failure in ten runs with 19 or 20 forced collections during profiling.
The earlier failure shows that a thread can go without samples long enough to break the 2 ms debt bound. Its cause is unknown.
GC-heavy workloads stay outside the supported model. The debt bound stays unchanged.

## Check limits

The current runner requires exactly 10 runs. The earlier run used an earlier runner revision with 10 runs.
A forced collection starts only while a worker reports that it is working. The count does not show how long each collection overlaps the work.
The checks do not measure prediction accuracy under GC. They do not cover JIT, tiering, ReJIT, thread abort, or process exit.

See [the support boundary](../../../docsSrc/Explanations/target-local-prediction.md) and [the prediction qualification](prediction.md).
