# Timer scheduler capture without forced GC

## Run and integrity

`./build.cmd LinuxFastTimerSchedulerNoGc` retained `20261015T064933025-76a6afeb` under `artifacts/timer-sampled/`.
The runner used prepared workers, a 10 microsecond initial interval, and the existing scheduler backend.
It ran zero and 50 percent once each with the explicit `no-gc` argument.
Both records have `forceGc = false`. Ordinary runtime GC remains possible.
The layout checks and zero-percent case passed.
Treatment failed with `Sampled timer debt exceeded 2000000 ns.`
Both cases completed native closure and scheduler shutdown.
All 79 manifest source hashes matched immediately after capture.

Each case contains three distinct thread identities in one process.
Every stream has zero final loss, no reported failure, ordered alternating switch records, and no censored interval.

| Case | Controller records | Selected records | Peer records |
| --- | ---: | ---: | ---: |
| Zero percent | 1,370 | 2,000 | 1,926 |
| Treatment | 34 | 48 | 170 |

The retained analysis uses the existing `AnalyzeScheduler.parse_capture` and `overlap` functions.
It examines the last raw observation for each journal, including failed observations when present.
All 3,951 treatment handler intervals fit inside the target streams' assured collection boundaries.
The zero-percent case has no handler interval.

## Handler observations

| Peer command / child | Requested wait (ns) | Measured wait (ns) | Handler interval (ns) | Observed switch-out overlap (ns) |
| --- | ---: | ---: | ---: | ---: |
| 460 / 7 | 50,000 | 1,446,201 | 1,451,992 | 0 |
| 450 / 8 | 50,000 | 928,572 | 936,774 | 0 |
| 1 / 6 | 50,000 | 306,161 | 308,948 | 0 |

The largest handler interval in this run stays below the 5 ms gate.
This run does not reproduce the [earlier 9.684 ms invalid wait](ledger-boundaries.md).
The observed Linux switch records do not explain the listed long handler intervals.
They cannot rule out host scheduling, clock effects, interrupt handling, or unobserved runtime effects.
No cause is assigned to the earlier failure.

## Debt checkpoint

The first violating checkpoint occurs at timestamp `394864753280699`.
It records global delay 199,020,598 ns, peer released wait 196,526,424 ns, and accepted active wait 50,112 ns.
The resulting peer debt is 2,444,062 ns.
Peer command 460 reserved 494,174 ns before subsequent global credit increased by 2,000,000 ns.

Command 460 was submitted at `394864749807534`.
Its first child started at `394864753186272`, a 3,378,738 ns interval after submission.
No controller, selected-target, or peer-target switch-out interval overlaps submission through the debt checkpoint.

At the checkpoint, child 1 had finished and child 2 was executing.
Child 2 started at `394864753257295` and finished at `394864753309543`.
Its elapsed prefix at the checkpoint is at most 23,404 ns.
Even if that entire prefix earned payment, the observed-clock debt would remain at least 2,420,658 ns.
Accepted-prefix lag alone therefore cannot remove this recorded violation under the observed clock ordering.
The larger later payment belongs after this checkpoint and cannot replace it.

The final journal has seven valid children and releases during shutdown.
Shutdown reduces recorded peer debt to 747,680 ns, without changing the failed outcome.
Neither extra payment nor successful cleanup establishes capacity at the earlier checkpoint.

## Reproduction and limits

The artifact directory retains `analyze-handler-scheduling.py` and its exact `AnalyzeScheduler.py` dependency.
Run the retained script with Python to reproduce `handler-scheduler-analysis.json` without a new timing run.
The analysis preserves failures and reports observed overlap only.
It does not establish scheduler capture cost, physical clock accuracy, intervention capacity, or prediction accuracy.
The next design decision must account for delayed service and batched credit without forgiving existing debt.

| Artifact | SHA-256 |
| --- | --- |
| `manifest.json` | `78f3c042f7c4c681c0f844779b6cd1fee2feadf1c621f6179dee9810d3cfb6ff` |
| `summary.json` | `ea5b16b74a009c87781d13ef70039f4347e0c186a87bddbc72806aefa39f26f4` |
| `sampled-0.json` | `643fb671cde04308c1467d04b04fea89258025c1b87d5b15ea0b486089492985` |
| `sampled-50.json` | `6e82336d81af4de75aa9bdb5fe1b48e1846f12339260fb5ef75f0d68eeea0ccd` |
| `handler-scheduler-analysis.json` | `6aa3a7586ae980456303b4dca167140568d73516e262de9050a5c3aae1f96c65` |
| `analyze-handler-scheduling.py` | `71b7ff98b8431f408382f22656c2c87e493154f554628d599d7cbb1f4001ce9d` |
| `AnalyzeScheduler.py` | `032e5db11c5d1a1500444f715bfae070eaad08f04e2fd7ee475e20a678c92365` |

See [scheduler observations](../../../lode/profiling/scheduler-records.md) and
[the sampled timer procedure](../../../docsSrc/How_Tos/run-sampled-timer.md).
