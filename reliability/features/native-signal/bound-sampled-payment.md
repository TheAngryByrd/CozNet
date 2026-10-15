# Bound sample-to-payment evidence

The workload feeds real CLR-bound Linux task-clock samples into peer cooperative payment.
Only the peer pays. The selected owner also runs the controller and retains debt from peer overshoot.
This diagnostic does not establish general eligibility, selected-owner payment, or prediction accuracy.

```powershell
dotnet run --project build/build.fsproj -- -t LinuxBoundSampledPayment
python native/BoundSampledPaymentAnalysisChecks.py
```

The workload targets .NET 10 and uses probe interval 1024 with runtime perf-map output disabled.
Its fixed case order is zero percent, 50 percent, then invalidation after real payment.
The normal cases request two seconds of selected work.
Gates remain 2 ms observed debt, 50 microseconds requested child wait, 5 ms handler interval, and 100 ms receipt upper bound.
The manifest retains source and binary fingerprints, the container image identity, and case settings.
The kernel log identifies the running container kernel.

## Retained outcomes

| Run under `artifacts/bound-sampled-payment/` | Result | Limit |
| --- | --- | --- |
| `20261015T122927095-b5d2f150` | Initial zero and 50-percent runtime checks pass. | Native fields serialized as empty objects. |
| `20261015T123306031-043ee6ac` | Zero and after-payment invalidation pass. Treatment fails at 2,499,933 ns peer debt. | Native fields are still missing. |
| `20261015T123957055-78443f07` | All three cases and independent replay pass with native fields. | One retained passing run does not establish stable capacity. |

The second run retains 259 earlier payments and pending command 260 at failure.
Its missing native fields prevent identifying whether that request was waiting, complete, or awaiting controller acceptance at the failed boundary.
The runtime gate failure remains evidence of rejected capacity. The later passing run does not erase it.

The serializer now includes public fields from the native header and child structs.
The analyzer rejects empty native objects and recomputes selected-period credit and child wait totals.
It also checks ownership, sample prefixes, native identities, receipt bounds, release copies, and debt arithmetic at retained observations.
It does not reconstruct scheduling between those observations.

## Complete evidence run

Run `20261015T123957055-78443f07` retains unchanged source and binary inputs.

| Case | Source samples | Credit | Accepted payments | Settled peer wait | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| Zero percent | 1,984 | 0 ns | 0 | 0 ns | Completed |
| Fifty percent | 1,990 | 808,500,000 ns | 1,617 | 808,500,078 ns | Completed |
| Invalidation after payment | 80 | 500,000 ns | 1 | 500,083 ns | Rejected as required |

The invalidation case credits only its consumed prefix of 22 records.
Its remaining source records are diagnostic evidence and do not add ledger credit.
Both completion paths reject the invalidated CLR binding. Cleanup joins the owner and releases the native session.

The 50-percent case records peak debt of 467,524 ns on the selected owner and 999,929 ns on the peer.
Its final selected residual debt is 78 ns. Peer residual debt is zero.
The source reports no lost records, lost samples, or throttling in the normal cases.

At that time, twelve native contracts, both managed contract groups, and five Python analyzer checks passed.
The analyzer checks include missing-field rejection, corrupted credit and payment rejection, and preservation of runtime failure.

Review added case binding, required debt evidence, continuation ordering, invalidation cleanup, and final-debt reconciliation to the analyzer.
At that review, all fifteen analyzer checks and offline replay of the retained complete-evidence run passed.
The reviewed replay, check log, and Python source hashes are retained beside the original run in `reviewed-analysis/`.
That review changed only the analyzer and its checks. The measured managed and native source fingerprints matched at that revision.

## Failure-boundary capture

The workload now retains diagnostic native evidence before cleanup changes request ownership.
The captured prefix does not settle payment or reduce debt. An executing child's uncommitted wait remains unknown.
Windows run `artifacts/cooperative-interop/20261015T131829093-a24433d2` passes all seven interop cases and the managed contract group.
The diagnostic case verifies active and retired capture without receipt tracking, followed by immutable evidence across request reuse.
The seventeen analyzer checks cover original-failure preservation, capture states, identity, clocks, and cleanup-prefix consistency.
Linux run `artifacts/cooperative-interop/20261015T132144062-00c3f487` passes the same seven cases and managed contract group.
Both platform runs pass twelve native contracts with unchanged inputs.
Live run `artifacts/bound-sampled-payment/20261015T132348027-8a4e0258` retains a 2,499,906 ns peer-debt failure.
Its capture records retired request 122 with 499,968 ns of native wait. Those waits did not settle in the ledger.
The same run exposed a fixture race that delivered invalid samples before `Start` returned.
The fixture now gates invalid delivery until after startup. Its completion rejection and cleanup assertions remain unchanged.
That first capture formatted the exception before reading native state. The helper now formats exceptions after capture.

Corrected run `artifacts/bound-sampled-payment/20261015T132712330-55ac6e00` passes both managed groups, twelve native contracts, and seventeen analyzer checks.
Zero percent completes. Invalidation after payment rejects as required and records no pending request at capture.
The treatment rejects native request 113 after 112 accepted payments.
Its invalid child retains 8,309,850 ns waited and an 8,310,379 ns handler interval, above the unchanged 5 ms bound.
The raw header records `Stop=5`, `Error=4`, and `Measured=0`. The ledger retains 999,859 ns of observed peer debt.
Offline replay verifies the evidence but preserves treatment failure. Neither run establishes stable capacity.
Both sampled runs predate the explicit managed timestamp-frequency field. They do not establish a cross-platform clock mapping.

## Next evidence

Capacity remains sensitive to scheduling and delayed receipt acceptance.
The current controller accepts only retired journals and does not subtract unconfirmed partial wait from debt.
A diagnostic change must retain native state at the failed debt boundary before attributing a failure to either payment or observation.
General Coz execution also needs selected-owner payment with explicit sampling and eligibility transitions.
Any later physical comparison must preserve the existing prediction gate and account for observer CPU.

See [the workload reference](../../../docsSrc/Reference/bound-sampled-payment.md) and [eligibility research](../../../docsSrc/Research/cooperative-sample-eligibility.md).
