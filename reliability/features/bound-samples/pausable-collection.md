# Pausable bound collection evidence

This record covers source pause ownership before selected-owner payment.
The live workload uses the real CLR-bound map, Linux task-clock source, pausable collector, and source-journal parser.
It performs excluded CPU work without a native payment request.

## Retained run

Run: `artifacts/bound-samples/20261015T145356387-fedade5b`.
The manifest records source and binary hashes, the SDK image, and the fixed probe interval of 1024.
`kernel.log` identifies the observed kernel. The source analysis uses a separately pinned kernel revision.

Command:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxBoundSamplesVerify
```

The five live cases passed: `collect`, `pause`, `before-enable`, `during`, and `after-close`.
Both portable check groups returned zero. The manifest inputs remained unchanged during the run.

The `pause` case completed two excluded callbacks and three source epochs.
Its immutable sample labels retained 505 records in epoch 1, 423 in epoch 2, and 952 in epoch 3.
The final cumulative CPU counter was 1,883,603,900 ns.
The disabled counter pairs remained at 505,973,100 ns and 929,159,000 ns during deliberate owner CPU work.
The final batch reconciled all 1,880 samples without reported loss or throttling.
The case also rejected a producer-stop call from a foreign OS thread before source enable.

The final counter-error fix also passes under `artifacts/bound-samples/20261015T150407510-021daf91`.
Its manifest pins the revised source. Both managed groups and all five live cases pass with unchanged inputs.
`ResumeCounterFailureRejectsCollection` covers a failed read that retains a regressed CPU value and an error.
Cleanup preserves the stop acknowledgement and records that failure under `counters`.

## Deterministic contracts

`--verify-pausable-managed` runs the collector, session parser, and pausable integration checks.
`BoundSamplesManagedVerify` runs this group and the existing one-shot collection group in sequence.
The pausable integration checks cover:

| Contract | Check |
| --- | --- |
| Queued records keep their old epoch; callback precedes resume | `FinalDrainPreservesEpochAndCallbackBoundary` |
| Callback failure keeps collection stopped | `CallbackFailureDoesNotResume` |
| CLR invalidation prevents resume | `InvalidationDoesNotResume` |
| Failed disable prevents the callback | `DisableFailurePreventsCallback` |
| Failed resume rejects final acceptance | `ResumeFailureRejectsCollection` |
| Late loss rejects earlier provisional work | `LateLossRejectsEarlierEpochs` |
| Known accounting mismatch prevents the callback | `InconsistentPausePreventsCallback` |
| Foreign control cannot change ownership | `WrongThreadControlIsRejected` |
| Failed enable still receives a producer-stop acknowledgement | `InitialEnableFailureStillStopsSource` |
| Counter failure does not change successful disable evidence | `CounterFailurePreservesDisableEvidence` |
| Post-enable counter failure rejects collection and preserves cleanup | `ResumeCounterFailureRejectsCollection` |
| Live epoch reads enforce lifecycle and offset boundaries | `EpochReadsPreserveOwnershipAndRejectInvalidOffsets` |

`MutableJournalInputsCannotChangeAcceptedEvidence` checks copied initial, enabled, disabled, and closed journals.
The [guard mutation checks](../../mutations/pausable-session-guards.md) detect removal of the expected-epoch and enabled-state guards.

These managed checks pass on Windows and Linux.

## Limits

The source retains its perf period remainder across resume.
Consumers must exclude the first observation in each new eligibility epoch from credit.
The workload does not settle selected debt or establish general CLR safety, payment capacity, or prediction accuracy.
The counter check does not exclude all profiler overhead from sample attribution.
Late loss remains possible and must reject the whole experiment, including irreversible earlier waits.

See [the source analysis](../../../docsSrc/Research/perf-pause-ownership.md) and [the collection reference](../../../docsSrc/Reference/bound-samples.md).
