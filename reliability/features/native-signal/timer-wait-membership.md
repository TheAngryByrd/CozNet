# Recorded timer wait membership evidence

The analyzer reuses captures from `artifacts/timer-sampled/20261015T044443270-12d19aae`.
No timed workload was repeated.
Reports are `timer-wait-membership-reviewed-0.json` and `timer-wait-membership-reviewed-50.json` in that directory.
Each report retains input and analyzer hashes. These reports are later analyses, outside the original collection manifest.

Both captures pass prefix reconciliation, observed clock checks, closure checks, and per-worker interval ordering.
The zero-percent capture has no recorded children and zero wait ranges.

| Treatment worker | Recorded children | Recorded wait | Observed window range | Frozen accepted delta |
| --- | ---: | ---: | ---: | ---: |
| Selected | 19 | 1,918 ns | [1,918, 1,918] ns | 1,918 ns |
| Peer | 16,316 | 812,502,014 ns | [812,502,014, 812,502,014] ns | 812,502,014 ns |

All positive recorded waits fall within the guaranteed observed window.
The later accepted deferred child contributes zero wait.
The treatment retains `Sampled timer debt exceeded 2000000 ns.` and native closure.
The exact ranges describe recorded overlap under the stated clock assumptions.
They do not establish missing-work bounds, instruction eligibility, physical-clock accuracy, or prediction accuracy.

Ten focused checks pass on Windows and Linux.
The exhaustive small-domain check enumerates wait placement and endpoint choices.
The late-acceptance fixture has 10 ns of recorded wait inside the window and a zero frozen accepted delta.
Nineteen prefix checks and thirty-one matched checks also pass on both platforms after the parsed child representation change.

See [the range contract](../../../docsSrc/Reference/timer-wait-membership.md) and [frozen-prefix evidence](frozen-timer-prefix.md).
