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
All fifteen current analyzer checks pass. Offline replay of the retained complete-evidence run also passes these stronger checks.
The reviewed replay, check log, and Python source hashes are retained beside the original run in `reviewed-analysis/`.
Only the analyzer and its checks changed after that live run. The measured managed and native source fingerprints still match.

## Next evidence

Capacity remains sensitive to scheduling and delayed receipt acceptance.
The current controller accepts only retired journals and does not subtract unconfirmed partial wait from debt.
A diagnostic change must retain native state at the failed debt boundary before attributing a failure to either payment or observation.
General Coz execution also needs selected-owner payment with explicit sampling and eligibility transitions.
Any later physical comparison must preserve the existing prediction gate and account for observer CPU.

See [the workload reference](../../../docsSrc/Reference/bound-sampled-payment.md) and [eligibility research](../../../docsSrc/Research/cooperative-sample-eligibility.md).
