# Cooperative comparison analysis

`native/AnalyzeCooperativeComparison.py` analyzes a retained `cooperative-matched-v1` directory.
`native/VerifyCooperativeComparison.ps1` collects the four arms in isolated Linux containers.
The first declared Linux block completes all four arms and remains unqualified.
It preserves pending treatment ownership and source tails. No prediction-accuracy claim follows from completion.

## Runner

| Parameter | Default | Meaning |
| --- | --- | --- |
| `EvidenceDirectory` | `artifacts/cooperative-comparison` | Parent for a new capture directory. |
| `SelectedBatches` | `50000` | Positive `Int32` selected batch count for every arm. |
| `ProbeInterval` | `1024` | Sampled-arm backedge interval. Accepts `1024` or `8192`. |
| `PythonCommand` | `python` | Python command for retained checks and analysis. |
| `PrepareReceipts` | Disabled | Prepare isolated managed receipt paths before both sampled measurements. |
| `OwnerController` | Disabled | Run both sampled controllers on the selected owner thread. |

The runner saves the plan and four unexecuted outcomes before preparation.
It snapshots source files, managed workload files, and the existing Linux profiler.
It builds the cooperative native library from the retained source with the resolved compiler image.
Native, analyzer, and managed preflight checks must pass before any arm starts.

Each arm receives read-only input mounts and its own writable output directory.
The runner records the attempt before launch. It retains process exits and copies available results to the capture root.
Each outcome retains `containerArguments`, per-arm `activation`, and workload `arguments`.
The common arguments include tiering, ReadyToRun, and perf-map settings.
Raw container arguments also contain absolute host mount paths. Capture files remain local artifacts.
Published reliability records use relative capture paths.
A failed arm does not prevent later arms from running.
Each process has a 30-second timeout and a two-second termination grace period.

The runner checks original inputs after capture and retained hashes before analysis.
It does not execute an analyzer whose retained hash changed.
The runner always returns failure while prediction qualification remains unmet, including after a complete collection.

`CooperativeComparisonChecks` runs portable checks without a timing experiment.
`LinuxCooperativeComparison` builds the Release workload, runs those checks, and invokes the runner.
Both targets preserve the existing MiniScaffold build graph.

## Inputs

| File | Contract |
| --- | --- |
| `plan.json` | Fixed order, positive selected batch count, `predictionErrorBound=0.02`, and no calibration claim. |
| `manifest.json` | Required source and binary snapshot hashes, plus a complete runtime image digest. |
| `outcomes.json` | One outcome per planned arm, in plan order. |
| `integrity.json` | Whether original inputs remained unchanged during capture. |
| `runner-errors.json` | Retained runner errors. An empty list means no recorded runner error. |
| `<mode>.json` | Retained workload result for that arm. |

The fixed order is `physical-baseline`, `physical-optimized`, `sampled-zero`, then `sampled-half`.
Each outcome contains `attempted`, `executed`, `recordExists`, and `exitCode`.
An executed process requires an attempt and an integer exit code.
An unexecuted arm has no exit code or retained process result.
Boolean values cannot replace integer counters or exit codes.
Duplicate JSON fields and non-finite numbers reject the input.
Sampled clock frequency must be positive before replay arithmetic starts.
An arithmetic failure within one arm invalidates that arm without discarding the other arm results.

Physical parsing checks checksum totals, fixed selected work, frozen progress, and absence of intervention accounting.

New plans declare Boolean `ownerController` and `controllerOwnershipEvidenceVersion=1`.
Both sampled arms must report matching Boolean `ownerControllerEnabled` metadata and pass the full selected-payment replay.
Missing or mismatched ownership metadata invalidates that arm and suppresses the aggregate comparison.
Legacy plans without the version marker permit only separate-controller execution.
Physical arms require no ownership metadata and receive no controller activation.
The default remains separate-controller execution. The new switch follows all existing positional parameters.
Sampled parsing uses the full selected-payment replay.
It requires finite measurement, qualified selected cadence, direct receipt readiness, and disabled selected handoff.

## Probe interval contract

New plans contain `probeIntervalEvidenceVersion=1` and an explicit integer `probeInterval`.
Both sampled records must contain an integer `probeInterval` equal to the plan.
A missing or mismatched value invalidates that arm and suppresses the descriptive comparison.
Physical arms do not require probe metadata because profiling remains disabled.

Legacy plans without the version marker accept only interval 1024, including an omitted plan interval.
Legacy sampled records can omit the interval. Present values must still match 1024.
Null, Boolean, floating-point, or unknown version markers reject the plan.
An unversioned 8192 plan is invalid.

## Receipt preparation contract

New plans contain `receiptPreparationEvidenceVersion=1` and an explicit Boolean `prepareReceipts`.
Both sampled arms receive the requested `COZNET_PREPARE_RECEIPTS` value.
Each sampled record must contain a Boolean `receiptPreparationEnabled` equal to the plan.
A missing or mismatched value invalidates that arm and suppresses the aggregate comparison.
Full selected-payment replay also checks the preparation result and its premeasurement timing.
Physical arms do not require preparation metadata and do not run the receipt exercise.

Legacy plans without the version marker permit only unprepared receipts.
They can omit `prepareReceipts` or set it to false.
Legacy sampled records can omit the activation field. A present value must be Boolean false.
Unknown or noninteger versions and non-Boolean plan settings reject the plan.
Prepared records cannot be used under a legacy unprepared plan.

## Output

`probeInterval` records the parsed plan interval. `probeIntervalEvidenceVersion` is 1 for the new contract and null for legacy plans.
The version describes required evidence. It does not prove collection success or callback reach.
`prepareReceipts` records the parsed preparation mode.
`receiptPreparationEvidenceVersion` is 1 for the new preparation contract and null for legacy plans.
Neither field qualifies prediction accuracy.

The CLI writes `analysis.json` and returns `1` while qualification remains unmet.
`passed` and `predictionAccuracyEstablished` remain `false`. `qualifiedComparison` remains `null`.

For a valid plan and outcome list, `cases` retains all declared arms, including missing and failed results.
`attempted` counts attempted processes. `completed` counts arms with accepted descriptive costs.
`invalid` counts invalid arms. `qualified` remains zero.
A collection integrity failure can invalidate the comparison even when all four arm records parse.

`descriptiveComparison` exists only when all four costs parse and no integrity or runner error remains.
Each cost divides effective duration bounds by frozen selected progress.
Effective duration subtracts the global-delay delta. It does not subtract summed worker waits.
The analyzer calculates ratios with exact fractions and rounds output bounds outward.
These bounds describe retained clock brackets. They are not accuracy bounds.

Portable checks cover input rejection, full sampled replay, interval arithmetic, failure retention, and comparison suppression.
Collection checks use controlled arm results to isolate orchestration behavior.
The first live block also passes retained replay and input integrity checks.
This single block does not establish general capture reliability or physical optimization calibration.

The runner snapshots source and prebuilt managed/profiler binaries independently.
Stable hashes do not prove that those binaries came from the retained source.
The build target rebuilds the managed workload before capture. Direct script use requires the same preparation.
Source-to-binary correspondence remains an unmet qualification requirement.

Physical startup, peer-fault, stop-timeout, and join-failure branches lack direct fault-injection checks.
Successful platform runs do not establish those failure paths.

## First diagnostic block

The descriptive prediction interval is approximately −2.4% to +0.3% elapsed reduction.
The physical interval is approximately +14.1% to +69.8%.
Boundary widths, attribution, local optimization calibration, and single-block ordering prevent accuracy qualification.
The [phase capture](../Explanations/cooperative-pause-cost.md) separates selected pause intervals and retained controller dispatch brackets.
It does not qualify observer cost or prediction accuracy.

See [physical work](cooperative-physical-work.md), [finite measurement](cooperative-measurement.md),
[comparison limits](../Explanations/cooperative-physical-comparison.md),
and [the capture procedure](../How_Tos/capture-cooperative-comparison.md).
