# Native target-local credit and payment

Evidence: `artifacts/perf-ring/20261018T134732260-d0a9ba43` (`credit-zero.json` and `credit-half.json`).

`TargetCreditWorkload` runs two native threads on kernel `5.15.153.1-microsoft-standard-WSL2` on x64.
Each thread owns a `LinuxTargetSession` with a credit sink and one shared `ExperimentDelay`.
The selected thread runs code in the `coz_selected` section. The peer runs code in the `coz_peer` section.
Both sections are payment-eligible. Only `coz_selected` is selected.
Each thread runs a fixed number of work rounds, calibrated to about 300 ms of thread CPU before the sessions start.
No managed controller or central thread credits or pays. Each thread credits and pays in its own handler.
`native/WriteTargetEvidence.py` writes this page from the run directory.

| Mode | Selected samples | Credited (ns) | Global delay (ns) | Children | Measured payment (ns) | Max observed debt (ns) | Replay |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `zero` | 302 | 0 | 0 | 0 | 0 | 0 | Valid |
| `half` | 302 | 151,000,000 | 151,000,025 | 2,990 | 151,000,025 | 999,999 | Valid |

| Mode | Selected wall (ns) | Peer wall (ns) | Peer local (ns) | Max selected handler (ns) | Max peer handler (ns) |
| --- | ---: | ---: | ---: | ---: | ---: |
| `zero` | 302,779,541 | 303,979,969 | 0 | 5,133 | 4,884 |
| `half` | 303,170,343 | 456,238,782 | 151,000,025 | 5,241 | 1,506,763 |

In `half`, each selected sample adds 500,000 ns. The selected thread publishes 302 times and never pays. The peer publishes its measured overshoot 204 times.
The peer pays 151,000,025 ns in 2,990 children. No child requests more than 50,000 ns.
The children request 148,788,819 ns. Measured overshoot makes up the rest of the measured payment.
The peer wall time grows by about 152 ms compared with `zero`. The experiment records no failure. Deferred-debt events in the journal: 1. The closing settlement runs at an ineligible address and can defer.

The replay orders all 4,255 events by sequence. Every sequence from 1 to 4,255 occurs once.
For each event, the replay requires the same prior local delay and an observed global delay that some publish produced.
It recomputes credit from the selected spans and requires the global delay to equal the largest publish.
Sorted by value, every publish must name the previous publish as its observed global delay, so no update is lost.
Each child must follow positive observed debt and must not request more than that debt.

## Limits

This is native code only. It does not test CLR execution, managed reconciliation, or prediction accuracy.
The peer pays in a spin, so payment consumes its CPU. Each settlement requests at most 2 ms. Measured overshoot can add more. The handler bound stays below 5 ms.
The two threads do not share work. The wall-time change is not a prediction of an optimization.
One run per mode. Other kernels and CPU counts are not verified.
The task-clock event keeps sampling during spin payment. The peer accepts 456 samples in `half` and 303 in `zero`. Payment samples are unselected and earn no credit, but they fill the journal.
Measured overshoot publishes as global delay. A preempted child on a loaded host can make other threads owe more than 2 ms and fail the experiment.
The final settlement at closure can only publish. Up to 2 ms of peer debt can stay unpaid at the end. The pass rule allows that residue.
Each replayed thread starts at local delay 0. The workload creates both accounts before either thread starts, so the global delay is 0 then.
The journal has no settlement identity, so the replay cannot check the 2 ms request cap for each settlement. The synthetic checks cover that cap.

See [the ledger reference](../../../docsSrc/Reference/target-delay-ledger.md) and [the plan](../../../lode/plans/target-local-credit.md).
