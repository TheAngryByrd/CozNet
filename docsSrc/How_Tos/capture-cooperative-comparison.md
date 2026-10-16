# How to capture a cooperative comparison

Use PowerShell 7, Python 3.10 or later, and Docker with Linux containers.
Prepare the existing Linux profiler and local images with the repository's native build procedure.
The required images are `coznet-native-build:10.0.100` and `mcr.microsoft.com/dotnet/sdk:10.0.100`.
The runner does not pull images.

The first reviewed Linux block completes all four arms and remains unqualified.
Complete the required code review before collecting evidence from a changed implementation.

1. Build the Release workload and run the portable checks:

   ```powershell
   dotnet run --project build/build.fsproj -- -t CooperativeComparisonChecks
   ```

2. Run one declared block:

   ```powershell
   pwsh -NoProfile -File native/VerifyCooperativeComparison.ps1 -SelectedBatches 50000
   ```

3. Retain the printed capture directory, including all failed arms and logs.
4. Inspect `outcomes.json`, `runner-errors.json`, and `integrity.json` before interpreting `analysis.json`.
5. Use `descriptiveComparison` only as a description of the retained records.

The expected runner result is a nonzero exit because prediction accuracy remains unqualified.
A nonzero exit alone does not distinguish a collection failure from a complete, unqualified collection.
Do not retry an unchanged timing failure to obtain a pass.

To prepare managed receipts in both sampled arms, add `-PrepareReceipts` to the runner command.
Use `-ProbeInterval 8192` to select the lower-density probe option.
Check `plan.json` for `prepareReceipts=true` and `receiptPreparationEvidenceVersion=1`.
Both sampled results must report `receiptPreparationEnabled=true` and retain their preparation evidence.
The physical arms continue to run without profiling or receipt preparation.

To use the selected owner as controller, add `-OwnerController` to the runner command.
Check `plan.json` for `ownerController=true` and `controllerOwnershipEvidenceVersion=1`.
Confirm that both sampled results report `ownerControllerEnabled=true`.
Combine `-OwnerController`, `-PrepareReceipts`, and `-ProbeInterval 8192` for the prepared owner/controller comparison.

To capture sample-clock brackets, add `-BoundSampleClock` to the runner command.
Check `plan.json` for `boundSampleClock=true` and `boundSampleClockEvidenceVersion=1`.
Check both sampled results for `boundSampleClockEnabled=true` and inspect their clock evidence.
Physical arms do not capture the sample clock.
After collection, analyze each sampled arm with separate output paths:

```powershell
python native/AnalyzeBoundSampleMembership.py <capture>/sampled-zero.json <new-zero-membership.json>
python native/AnalyzeBoundSampleMembership.py <capture>/sampled-half.json <new-half-membership.json>
```

Keep clock rejection separate from workload failure.
Do not use source-time membership to add retrospective credit or claim prediction accuracy.

See [the reference](../Reference/cooperative-comparison.md) for field contracts
and [the explanation](../Explanations/cooperative-physical-comparison.md) for comparison limits.
