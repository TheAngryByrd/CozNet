# CozNet documentation

The public engine supports serial, synchronous CPU regions. Native service and sampled profiling remain experimental.

## Tutorials

- [Run your first serial experiment](Tutorials/Getting_Started.md).

## How-to guides

- [Run the sampled timer experiment](How_Tos/run-sampled-timer.md).

- [Verify the deterministic contract](How_Tos/verify-the-contract.md).
- [Inspect measurement evidence](How_Tos/inspect-measurements.md).
- [Verify native service accounting](How_Tos/verify-native-service.md).
- [Run the native sampled service experiment](How_Tos/run-native-sampled-service.md).
- [Check target-local timer delivery](How_Tos/run-target-lease-delivery.md).
- [Check target-local timer reuse](How_Tos/run-target-lease-reuse.md).
- [Verify timer interop](How_Tos/verify-timer-interop.md).
- [Check timer payment during managed execution](How_Tos/run-managed-timer-payment.md).
- [Check timer delivery during runtime transitions](How_Tos/run-timer-transitions.md).

## Reference

- [Measurement boundaries](Reference/measurement-boundaries.md): immutable endpoints, capture failures, and unresolved boundary uncertainty.
- [Matched physical workload](Reference/matched-physical.md): shared workers, physical mode selection, and frozen progress.

- [Sampled timer workload](Reference/sampled-timer.md): CPU sample credit, timer ownership, and retained failures.

- [Timer service controller draft](Reference/timer-service-controller.md): transport boundary, slot ownership, and unverified failure behavior.

- [Native service accounting](Reference/native-service-accounting.md): request states, journal rules, timing limits, and platform scope.

- [Target-local lease contract](Reference/target-local-lease.md): portable states, bounded children, and retirement ownership.
- [Target-local timer interop](Reference/timer-interop.md): C ABI, managed receipt bounds, and cleanup ownership.
- [Timer journal accounting](Reference/timer-accounting.md): ledger binding, accepted prefixes, and exact-once settlement.
- [Timer transition workload](Reference/timer-transitions.md): scenario reach, records, bounds, and scope.
- [Timer worker lifecycle](Reference/timer-worker-lifetime.md): retirement, join, release, and fault-check scope.

## Explanation

- [Serial proportional-delay model](Explanations/Background.md).
- [Native service ownership and measurement validity](Explanations/native-service-validity.md).
- [Causal profiling research](Research/causal-profiling-dotnet.md).
- [Native owner scheduling and local debt payment](Research/native-owner-scheduling.md).
- [Target-local timer delivery design](Research/target-local-timer-delivery.md).

See the [README](../README.md) for project setup. Agent project knowledge lives in the [Lode](../lode/lode-map.md).
