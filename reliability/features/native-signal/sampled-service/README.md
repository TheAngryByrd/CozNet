# First native sampled service run

The [retained run](summary.json) fails the unchanged service capacity contract.
The zero-speedup case passes. The 50-percent case fails during warmup before measurement.
Sources and binaries remained unchanged during the run, as recorded in [the manifest](manifest.json).
The original output directory is `artifacts/linux-sampled-service/20261014T162342451-242ed427`.

| Case | Result |
| --- | --- |
| Native checks | Six checks passed. |
| Managed accounting checks | Peer, selected, and five controller checks completed with exit code zero on Linux. |
| Zero speedup | Protocol and capacity passed. Targets joined and the session closed. |
| 50 percent | Peer lease preparation rejected 19,999,953 ns of observed debt during warmup. Targets joined and the session closed. |

The treatment settled one native peer command before failure.
Its 20 children measured 1,000,047 ns against a 1,000,000 ns lease.
The first controller service interval lasted approximately 36.68 ms.
The failure boundary recorded 21,000,000 ns of selected credit and no pending service commands.
The failure occurred before the loop could update its ordinary peak-debt fields.
The failure boundary therefore supplies the debt evidence; an empty `capacityViolations` array does not indicate a passing run.

These observations do not isolate JIT, parsing, sampling, or scheduling cost.
They do not establish native service capacity or prediction accuracy.
The workload has not been rerun to obtain a pass.

The controller checks use a fake backend and cover these contracts:

| Contract | Check |
| --- | --- |
| Prefix accounting and exact-once settlement | `PeerPrefixesSettleOnce` |
| Rejected receipt preserves raw evidence | `InvalidReceiptRetainsRawJournal` |
| Invalid state fails before native submission | `InvalidStateCannotSubmit` |
| Ambiguous submission retains ownership | `AmbiguousSubmissionRetainsOwnership` |
| Failed native release retains ownership | `FailedReleaseRetainsOwnership` |

The Windows `ServiceAccountingVerify` target also passed before the final boundary-record change.
The Linux run built and checked that boundary-record change.
No physical prediction comparison ran.

See [the native service reference](../../../../docsSrc/Reference/native-service-accounting.md).
