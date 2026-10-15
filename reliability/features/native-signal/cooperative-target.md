# Cooperative transport contract checks

The portable transport uses real worker threads with controlled clock and wait hooks.
The checks verify ownership and accounting. They do not measure intervention latency or exercise injected .NET probes.

| Requirement | Evidence in `CooperativeTargetChecks.cpp` |
| --- | --- |
| Registration bounds, code version, and controller/owner roles | `registrationAndRoleChecks` |
| One child per poll, exact measured payment, reentry rejection, snapshot-before-release, and generation reuse | `finiteChildrenAndGenerationReuse` |
| Active code guard, concurrent snapshot, and cancellation during a wait | `revocationCannotPassExecutingChild` |
| Cancellation before claim, revoked pending code, and failed-clock evidence | `pendingRevocationAndClockFailureRetainEvidence` |
| Distinct interrupted and failed wait accounting | `interruptedAndFailedWaitsKeepDistinctAccounting` |
| Zero clock reading before a wait prevents the wait, retains zero payment, and requires a copied journal before release | `preWaitClockFailureRetainsEvidence` |

Commands:

```powershell
./native/VerifyServiceOwner.ps1
./native/VerifyServiceOwner.ps1 -Platform LinuxContainer
```

Both commands pass all nine CTest entries on the current Windows and Linux x64 environments.
`cooperative-target` is the new entry. The existing eight lease, mailbox, snapshot, and service-owner entries also pass.
No live timing comparison ran. No capacity, receipt-latency, or prediction gate changed.

The [cooperative ABI checks](cooperative-interop.md) cover the production clock/wait adapter and native receipt records.
The next integration needs managed receipt tracking, ledger binding, and .NET probe binding.
The host must tie successful code revocation to actual CLR code lifetime before claiming that lifetime is protected.
The numeric site identity alone cannot prove probe origin.

See [the transport reference](../../../docsSrc/Reference/cooperative-target.md)
and [the lifecycle plan](../../../lode/plans/experiment-lifecycle.md).
