# CozNet documentation

The public engine supports serial, synchronous CPU regions. Native service and sampled profiling remain experimental.

## Tutorials

- [Run your first serial experiment](Tutorials/Getting_Started.md).

## How-to guides

- [Verify cooperative interop](How_Tos/verify-cooperative-interop.md).
- [Verify injected probe identity](How_Tos/verify-probe-identity.md).
- [Verify CLR-bound cooperative payment](How_Tos/verify-bound-cooperative.md).
- [Verify CLR-bound CPU samples](How_Tos/verify-bound-samples.md).
- [Run sample-driven bound peer payment](How_Tos/run-bound-sampled-payment.md).
- [Run bound selected-owner payment](How_Tos/run-bound-selected-payment.md).
- [Compare bound selected cadence](How_Tos/run-bound-selected-cadence.md).

- [Run the sampled timer experiment](How_Tos/run-sampled-timer.md).
- [Run physical CPU calibration](How_Tos/run-physical-calibration.md).
- [Run interleaved CPU calibration](How_Tos/run-interleaved-calibration.md).
- [Collect matched prediction diagnostics](How_Tos/run-matched-prediction.md).

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

- [Pausable sample collector](Reference/pausable-sample-collector.md) for the internal drain-ownership component under development.
- [Bound CPU session](Reference/bound-cpu-session.md) for repeated source transitions and cumulative counter evidence.

- [Managed cooperative receipts and accounting](Reference/cooperative-managed.md): first receipt bounds, exact site binding, and cleanup ownership.
- [Injected probe identity](Reference/probe-identity.md): literal sites and permanent CLR invalidation.
- [CLR-bound cooperative payment](Reference/bound-cooperative.md): lifetime admission, receipt acceptance, and cleanup.
- [CLR-bound CPU samples](Reference/bound-samples.md): source epochs, provisional records, and guarded completion.
- [Sample-driven bound peer payment](Reference/bound-sampled-payment.md): live credit, peer requests, debt gates, and invalidation.
- [Bound selected cadence](Reference/bound-selected-cadence.md): fixed work and sample-qualified check admission.

- [Cooperative C ABI](Reference/cooperative-interop.md): portable records, native receipt bounds, and production timing.

- [Cooperative target transport](Reference/cooperative-target.md): thread ownership, code-lifetime hooks, and finite poll service.

- [Conditional service completion range](Reference/service-completion-range.md): valid completion states and unsupported cases.

- [Observed sample membership](Reference/sample-membership.md): source timestamps, endpoint ambiguity, and nominal weight totals.

- [CPU counter evidence](Reference/cpu-counter-evidence.md): cumulative samples, transition journals, and endpoint count ranges.
- [CPU sample clock](Reference/cpu-sample-clock.md): explicit Linux timestamp selection and ABI checks.
- [Measurement boundaries](Reference/measurement-boundaries.md): immutable endpoints, capture failures, and unresolved boundary uncertainty.
- [Matched physical workload](Reference/matched-physical.md): shared workers, physical mode selection, and frozen progress.
- [Physical CPU calibration](Reference/physical-calibration.md): fixed-work pairs, retained failures, and statistical limits.
- [Interleaved CPU calibration](Reference/interleaved-calibration.md): short segments, block statistics, and overhead sensitivity.
- [Matched prediction diagnostics](Reference/matched-prediction.md): frozen costs, declared cases, and rejected attribution gaps.

- [Sampled timer workload](Reference/sampled-timer.md): CPU sample credit, timer ownership, and retained failures.

- [Timer service controller draft](Reference/timer-service-controller.md): transport boundary, slot ownership, and unverified failure behavior.

- [Native service accounting](Reference/native-service-accounting.md): request states, journal rules, timing limits, and platform scope.

- [Target-local lease contract](Reference/target-local-lease.md): portable states, bounded children, and retirement ownership.
- [Target-local timer interop](Reference/timer-interop.md): C ABI, managed receipt bounds, and cleanup ownership.
- [Timer journal accounting](Reference/timer-accounting.md): ledger binding, accepted prefixes, and exact-once settlement.
- [Timer transition workload](Reference/timer-transitions.md): scenario reach, records, bounds, and scope.
- [Timer worker lifecycle](Reference/timer-worker-lifetime.md): retirement, join, release, and fault-check scope.

## Explanation

- [Limits on valid service completion](Explanations/service-completion-limits.md).

- [Experiment boundaries and unpublished delay](Explanations/experiment-lifecycle.md).

- [CPU periods and missing tails](Explanations/cpu-counter-attribution.md).
- [Sample time and measurement time](Explanations/sample-clock-alignment.md).
- [Calibration variation and overhead sensitivity](Explanations/calibration-sensitivity.md).
- [Matched physical prediction](Explanations/matched-prediction.md).

- [Serial proportional-delay model](Explanations/Background.md).
- [Native service ownership and measurement validity](Explanations/native-service-validity.md).
- [Causal profiling research](Research/causal-profiling-dotnet.md).
- [Why cooperative probes need a CLR lifetime contract](Research/cooperative-clr-lifetime.md).
- [How native ranges support bound probes](Research/bound-native-mapping.md).
- [Cooperative payment and sample eligibility](Research/cooperative-sample-eligibility.md).
- [Perf pause and sample ownership](Research/perf-pause-ownership.md).
- [Native owner scheduling and local debt payment](Research/native-owner-scheduling.md).
- [Target-local timer delivery design](Research/target-local-timer-delivery.md).

See the [README](../README.md) for project setup. Agent project knowledge lives in the [Lode](../lode/lode-map.md).
