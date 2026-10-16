# Managed owner/controller mode

`COZNET_OWNER_CONTROLLER=1` selects the internal cooperative owner/controller mode.
An absent value or `0` keeps the separate controller worker. Other values fail parsing.
This setting does not extend the public serial engine.

## Sampled workload

The mode requires selected-owner payment.
Scheduler capture and dispatch timing are incompatible with this mode and fail before native session creation.
Their current records cannot represent shared scheduler identity or direct controller operations.

The selected caller creates the native session and owns reserved slot 1.
The peer retains its separate thread and slot 0.
Submission, receipt acceptance, and release execute directly on the selected caller.
The selected receipt interval still ends after acceptance and before release.
Code-lifetime checks and all debt, lease, child, handler, and receipt limits remain unchanged.

The result records `ownerControllerEnabled` as a Boolean.
Replay rejects a non-Boolean mode or incompatible enabled features.
Old records without the field retain separate-controller interpretation.
`controllerJoined=true` means that no separate controller worker remains. In owner mode, no such worker is created.
Cleanup retires the selected registration and joins the separate peer before native close.

## Controlled CLR checks

`VerifyCooperativeInterop.ps1 -BoundCooperative -OwnerController` selects the same native mode for one controlled slot.
The caller owns slot 0. No separate owner worker is created.
The paid case checks settlement and generation reuse.
The other cases invalidate CLR identity before submission, before polling, before receipt acceptance, or after settlement.
Each record includes the requested mode, and the runner checks that it matches.
Here, `joined=true` means that no separate owner worker remains.

MiniScaffold exposes `BoundOwnerControllerVerify` and `LinuxBoundOwnerControllerVerify`.
The existing bound verification targets retain separate ownership.

## Current limits

Twenty controlled CLR-bound cases pass on Windows x64 and Linux x64 with owner/controller mode enabled.
These cases use one target. A separate Linux sampled diagnostic passes zero-speedup, 50-percent, and expected-invalidation checks.
The sampled diagnostic exercises the selected owner and separate peer. Its measurement remains unqualified.
Sampled capacity, attribution, and performance benefit remain unverified.
Owner waits occupy the controller thread, so a comparison must inspect peer service as well as selected pause cost.
Native entry guards add cost in both modes.

The [controlled evidence](../../reliability/features/native-signal/managed-owner-controller.md) records both modes and their limits.

See [the native ABI](cooperative-interop.md),
[the design and timing risks](../Research/cooperative-owner-controller.md),
and [selected payment](bound-sampled-payment.md).
