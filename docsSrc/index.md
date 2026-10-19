# CozNet documentation

See [why direct receipt readiness remains experimental](Explanations/receipt-readiness.md) for the latest polling comparison.

The public engine supports serial, synchronous CPU regions. Native service and sampled profiling remain experimental.
The target-local profiler predicts optimizations in two-thread CPU-bound fork/join and barrier programs on Linux x64. Host scheduling delay can break a qualification run. See [the tutorial](Tutorials/Target_Local_Prediction.md).

## Tutorials

- [Run your first serial experiment](Tutorials/Getting_Started.md).
- [Predict an optimization with target-local sampling](Tutorials/Target_Local_Prediction.md).

## How-to guides

- [Analyze bound sample membership](How_Tos/analyze-bound-sample-membership.md).

- [Capture drain and service CPU](How_Tos/capture-component-cpu.md).

- [Capture selected-thread CPU](How_Tos/capture-selected-cpu.md).

- [Capture nominal-event credit](How_Tos/run-nominal-event-credit.md).

- [Measure recurring cooperative service](How_Tos/measure-cooperative-service.md).

- [Compare managed receipt preparation](How_Tos/compare-receipt-preparation.md).

- [Measure cooperative receipt cost](How_Tos/measure-cooperative-receipt-cost.md).

- [Capture a cooperative comparison](How_Tos/capture-cooperative-comparison.md).

- [Capture a finite cooperative measurement](How_Tos/capture-cooperative-measurement.md).

- [Compare selected scheduling handoff](How_Tos/compare-selected-handoff.md).

- [Capture bound scheduler evidence](How_Tos/capture-bound-scheduler.md).

- [Analyze cooperative peer progress](How_Tos/analyze-cooperative-peer-progress.md).

- [Verify cooperative interop](How_Tos/verify-cooperative-interop.md).
- [Verify injected probe identity](How_Tos/verify-probe-identity.md).
- [Verify CLR-bound cooperative payment](How_Tos/verify-bound-cooperative.md).
- [Verify CLR-bound CPU samples](How_Tos/verify-bound-samples.md).
- [Run sample-driven bound peer payment](How_Tos/run-bound-sampled-payment.md).
- [Run bound selected-owner payment](How_Tos/run-bound-selected-payment.md).
- [Compare bound selected cadence](How_Tos/run-bound-selected-cadence.md).

- [Run the sampled timer experiment](How_Tos/run-sampled-timer.md).
- [Capture nominal timer credit](How_Tos/run-nominal-timer-credit.md).
- [Run physical CPU calibration](How_Tos/run-physical-calibration.md).
- [Run interleaved CPU calibration](How_Tos/run-interleaved-calibration.md).
- [Collect matched prediction diagnostics](How_Tos/run-matched-prediction.md).

- [Verify the deterministic contract](How_Tos/verify-the-contract.md).
- [Inspect measurement evidence](How_Tos/inspect-measurements.md).
- [Verify native service accounting](How_Tos/verify-native-service.md).
- [Verify the Linux perf ring adapter](How_Tos/verify-perf-ring.md).
- [Check target-local payment under forced GC](How_Tos/verify-target-local-transitions.md).
- [Run every target-local suite as one package](How_Tos/qualify-target-local.md).
- [Profile your own fork/join program](How_Tos/profile-fork-join-program.md).
- [Run the native sampled service experiment](How_Tos/run-native-sampled-service.md).
- [Check target-local timer delivery](How_Tos/run-target-lease-delivery.md).
- [Check target-local timer reuse](How_Tos/run-target-lease-reuse.md).
- [Verify timer interop](How_Tos/verify-timer-interop.md).
- [Check timer payment during managed execution](How_Tos/run-managed-timer-payment.md).
- [Check timer delivery during runtime transitions](How_Tos/run-timer-transitions.md).

## Reference

- [Native perf sample decoder](Reference/native-perf-decoder.md): record layouts, terminal errors, and the Linux ring adapter.
- [Target sample dispatch](Reference/target-sample-dispatch.md): dispatch budgets, outcomes, epochs, and the Linux notification session.
- [Target delay ledger](Reference/target-delay-ledger.md): native credit, publication, bounded payment, and the event journal.
- [Target-local API](Reference/target-local-api.md): the native C ABI, the managed replay, and the sample program.

- [Bound sample membership](Reference/bound-sample-membership.md).

- [Cooperative component CPU](Reference/cooperative-component-cpu.md): drain and service CPU buckets and observer limits.

- [Selected-thread CPU capture](Reference/selected-cpu.md).

- [Nominal-event credit](Reference/nominal-event-credit.md): explicit statistical event weights and policy evidence.

- [Cooperative service timing](Reference/cooperative-service-timing.md): activity buckets, measurement subsets, and timing limits.

- [Managed owner/controller mode](Reference/owner-controller.md): activation, ownership, and unsupported diagnostic combinations.

- [Cooperative comparison analysis](Reference/cooperative-comparison.md): retained arms, input integrity, and descriptive bounds.

- [Cooperative physical work](Reference/cooperative-physical-work.md): shared work, command inputs, and frozen output fields.

- [Finite cooperative measurement](Reference/cooperative-measurement.md): frozen estimator inputs and invalid outcomes.

- [Selected scheduling handoff](Reference/selected-handoff.md): bounded attempts, unchanged accounting, and prediction limits.

- [Bound scheduler capture](Reference/bound-scheduler-capture.md): owner identities, clock bounds, and observed switch-out overlap.

- [Cooperative peer progress analysis](Reference/cooperative-peer-progress.md): conditional child timing from retained submit brackets.

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

- [Target-local sample processing](Explanations/target-local-sample-processing.md): upstream designs, final loss evidence, and remaining runtime contracts.
- [Target-local prediction](Explanations/target-local-prediction.md): why the fork/join model has an exact answer, and the support boundary.

- [CPU time and the prediction gap](Explanations/selected-cpu.md).

- [Cooperative pause and controller costs](Explanations/cooperative-pause-cost.md).

- [Synchronous cooperative owner and controller](Research/cooperative-owner-controller.md): native mode and pending managed integration.

- [Comparing cooperative work with a physical change](Explanations/cooperative-physical-comparison.md).

- [Owner draining and payment capacity](Explanations/owner-drain-capacity.md).
- [Observer overhead in the bound workload](Explanations/bound-observer-overhead.md).

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
