# Native owner/controller mode checks

The native ABI can reserve one slot for the session creator.
The default open function retains separate ownership.
This report does not establish managed integration, prediction accuracy, or lower controller cost.

## Checked behavior

| Check | Evidence |
| --- | --- |
| Default role rejection and explicit registration | `controllerOwnerLifecycle` and existing `registrationAndRoleChecks` |
| Child bounds, measured payment, copy before release, generation reuse | `controllerOwnerLifecycle` |
| Separate peer can execute while the creator services its slot | `controllerOwnerLifecycle` |
| Cancellation and code revocation retain zero-wait cleanup | `controllerOwnerLifecycle` |
| Clock and wait callbacks cannot recursively mutate transport or revoke code | `controllerOwnerRejectsReentrancy` |
| Reserved slot, foreign-thread rejection, buffer and release ownership | `controllerOwnerApiContract` |
| Registered controller slot with no peer can retire and close | `controllerOwnerPartialSetupCloses` |

The target checks use a controlled clock and exact expected payment values.
The ABI check uses production timing and separate worker ownership.
Each process has a 30-second CTest timeout.

## Executed commands

```powershell
./native/VerifyServiceOwner.ps1 -Platform Host -OutputDirectory artifacts/cooperative-owner-native/host
./native/VerifyServiceOwner.ps1 -Platform LinuxContainer -OutputDirectory artifacts/cooperative-owner-native/linux
```

Both commands passed all 16 native CTest cases with zero failures.
Windows used MSVC. Linux used Clang in the existing native build container.
Logs remain in `artifacts/cooperative-owner-native-host-review3.log` and `artifacts/cooperative-owner-native-linux-review3.log`.
Earlier 15-case and 16-case checks also passed.
The final checks strengthen cross-target reentry and cleanup after one slot registers successfully.

## Limits

The production ABI does not expose injectable timing callbacks.
Reentrant callback checks therefore exercise the direct native transport.
The CPU copy, release, and revocation assertions use a second target with a copied retired journal and a separate unlocked code lifetime.
The check then proves that this target can copy CPU records, release, and submit again.
The API entry guard has source review but no injected nested-call execution check.
This is accepted because the production ABI has no callback surface. A future callback surface requires direct nested-entry checks.
Nested calls that pass argument checks receive `BUSY` at the shared entry boundary.
A callback must not retry that result in a loop.
The new guards add unmeasured thread-local access cost in both modes.
Owner waits occupy the controller thread. Managed integration must check the resulting peer-service intervals.
Atomic readiness and progress queries remain observations. They do not grant settlement or release authority.

No macOS build or runtime check was performed.
The managed receipt endpoint, CLR checks, cleanup adapters, and sampled workload have not adopted this mode.
No timing capture was repeated to seek a pass.

See [the ABI contract](../../../docsSrc/Reference/cooperative-interop.md),
[the target contract](../../../docsSrc/Reference/cooperative-target.md),
and [the integration design](../../../docsSrc/Research/cooperative-owner-controller.md).
