# Live ledger boundary capture

## Retained run

`./build.cmd LinuxFastTimerCapacity` retained `20261015T063606544-4706e5cb` under `artifacts/timer-sampled/`.
The run used prepared workers, a 10 microsecond initial timer interval, and no controller phase capture.
It ran each percentage once.
The layout checks and zero-percent workload exited zero.
The 50-percent workload exited one.
The runner reported `inputsUnchanged = true`.
An independent comparison matched all 79 source hashes immediately after the run.

The source included the uncommitted conditional-range prototype.
The workload does not call that function.
This run exercises the ledger boundary producer from commit `cb1d30f`, not conditional-range integration.

## Frozen values

Both cases captured complete start and end records with both worker ledger readings.
Both started with zero global and local credit, no service, and no accepted prefix.
The zero-percent case ended with zero credit and completed native closure.

The treatment end record contains these values in nanoseconds:

| Worker | Global | Settled local | Accepted unsettled wait | Phase |
| --- | ---: | ---: | ---: | --- |
| Selected | 6,192,673 | 5,500,000 | 350,382 | delaying |
| Peer | 6,192,673 | 6,192,673 | 0 | runnable |

The selected prefix has seven children in command 7, generation 1.
Its 692,673 ns lease remains unsettled.
The peer has six released journals and no pending lease.
The frozen selected prefix retains its original failure and accepted count through subsequent cleanup attempts.

## Treatment failure

Selected child 8 requested 50,000 ns and recorded 9,684,223 ns of wait.
Its handler interval was 9,689,735 ns.
Both values exceed the unchanged 5,000,000 ns gate.
The final attempted journal marks that child invalid and retains only 350,382 ns as its measured total.
The managed adapter rejects the journal with `The timer journal contains a failure or invalid state.`
Cancellation also reports native status 16.

Both worker threads joined and retired.
The selected slot did not release, one pending journal remains, and `closed` is false.
These facts reject native completion qualification.
They do not establish whether scheduler preemption, runtime activity, or another cause produced the long wait.

The conditional valid-journal limit for this budget is 5,692,672 ns.
It cannot bound the actual invalid child or authorize settlement of its wait.
This run therefore establishes neither intervention capacity nor prediction accuracy.
No unchanged timing case was repeated.

## Artifact identities

| File | SHA-256 |
| --- | --- |
| `manifest.json` | `ac59ef206de698edfa62c094536700c5149ab6a6c8f16060f3b02e8450341850` |
| `summary.json` | `f0a3364b48a50302cde78028c032351b26fe54f66ef118e3f833fcc140a40c66` |
| `sampled-0.json` | `a5f5d2bd0ac9c3c8eeb48aaf604d713bc980629132aed06c3340f52a1076891a` |
| `sampled-50.json` | `b80d6d9a1a1c4095892e21dd557c9c2e9b09984f5c15f577aef2739ced109ef9` |

See [the boundary reference](../../../docsSrc/Reference/measurement-boundaries.md),
[valid completion limits](../../../docsSrc/Explanations/service-completion-limits.md), and
[the lifecycle plan](../../../lode/plans/experiment-lifecycle.md).
