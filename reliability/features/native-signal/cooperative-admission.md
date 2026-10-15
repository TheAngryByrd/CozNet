# Cooperative poll-site admission

The portable lease core now accepts a distinct cooperative-site plan.
The site contains nonzero worker, code-version, and site identities.
It does not prove that a caller executes on the registered thread or at that code location.
Runtime binding and cooperative delivery remain unimplemented.

## Contract checks

`TargetLeaseChecks` exercises the same prepare, publish, snapshot, and retirement path for both admission kinds.
The new checks initially failed compilation because the old core had no cooperative admission API.
The implemented tree passes all eight portable CTest entries on Windows and Linux x64.
These entries include the existing mailbox, snapshot, and service-owner checks.

| Requirement | Evidence in `TargetLeaseChecks.cpp` |
| --- | --- |
| Reject zero identities and invalid lease bounds | `cooperativeSitesAndPlansAreParsed` |
| Reject wrong kinds and stale generations without consuming delivery | `wrongAdmissionKindDoesNotConsumeLease` |
| Clear stale site evidence when timer storage is reused | `wrongAdmissionKindDoesNotConsumeLease` |
| Defer mismatched worker, code-version, or site identities without payment | `mismatchedCooperativeSitesDeferWithoutPayment` |
| Retain site evidence, bounded requests, overshoot, and retirement rules | `cooperativeChildrenRetainSiteAndBounds` |
| Preserve cancellation, invalid-wait rejection, and the child-duration bound | `cooperativeCancellationAndInvalidWaitPreserveAccounting` |

Commands:

```powershell
./native/VerifyServiceOwner.ps1
./native/VerifyServiceOwner.ps1 -Platform LinuxContainer
./build.cmd LinuxTimerInterop
```

The timer interop run is `artifacts/timer-interop/20261015T073602962-450a8c0b`.
Its layout, receipt, late-receipt, and clock-failure cases all exit zero with retained records.
The native observations in these cases contain deferred children with zero payment.
They do not verify live cooperative execution or paid timer service.
Its source manifest remains unchanged during execution.
The timer C ABI remains address-based and cannot represent cooperative-site evidence.
No cooperative observation enters the managed timer parser in this change.

## Limits

The added site value increases portable journal storage and copy size.
The interop checks establish behavior for their controlled cases, not unchanged timing cost.
No sampled capacity or physical-prediction workload ran for this change.
The earlier debt and prediction failures remain unresolved.

The next adapter must bind real target ownership, code lifetime, and probe provenance.
It must use a cooperative receipt contract rather than substitute a fake interrupted address.
The existing mailbox can retain publication and lifetime ownership across that work.
One cooperative poll must service at most one bounded child before returning to application code.

See [the lease reference](../../../docsSrc/Reference/target-local-lease.md)
and [the lifecycle plan](../../../lode/plans/experiment-lifecycle.md).
