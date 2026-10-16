# Cooperative physical work

The four-arm runner accepts opt-in `-PrepareReceipts` for both sampled arms.
New plans bind Boolean `prepareReceipts` with `receiptPreparationEvidenceVersion=1`.
Both sampled records must contain matching Boolean activation metadata and pass full preparation replay.
Physical arms need no preparation metadata. Legacy plans permit only unprepared sampled records.
Missing or mismatched sampled metadata preserves the other arm results and suppresses the aggregate comparison.
The switch follows existing parameters, preserving positional calls. The default remains unprepared.
The [prepared physical block](../../reliability/features/native-signal/prepared-physical-comparison.md) completes all four arms with unchanged inputs.
Sampled effective elapsed reduction is 9.5244–9.8421%, versus 51.5845–51.5957% physically. Prediction remains unqualified.
Treatment freezes peer command 203 with budget 499,886 ns and no accepted child.
Its lifetime service executions are 39,926, versus 231 at zero speedup. These counts are not durations or frozen-window totals.
Recurring pending-request service cost needs separate investigation. First-use preparation has not resolved the mismatch.

The [8192 comparison](../../reliability/features/native-signal/sparse-probe-comparison.md) completes all four arms with matching interval evidence and unchanged inputs.
Its descriptive sampled reduction is −14.4322% to −13.9043%, against 46.5633% to 46.5761% physically.
Treatment freezes pending peer command 231 with budget 499,895 ns. The comparison remains unqualified.
Selected pause brackets total 19.943184 ms. The first peer request bracket is 27.378076 ms.
Those wall-time observations do not identify CPU cost or a scheduling cause. First-use receipt work needs separate investigation.
The final checks preserve old positional runner arguments and reject legacy-plan interval mismatches.

`CooperativeCpuWork` shares checksum arithmetic and atomic progress publication between physical and bound sampled routes.
The protocol is `cooperative-checksum-v1`. Each batch must produce `49995000` before progress publication.
The physical route runs selected work on the caller and peer work on a dedicated worker.
Only selected arithmetic changes in optimized mode.

```text
--cooperative-physical <output.json> <baseline|optimized> <batches>
```

```mermaid
flowchart LR
    Warmup --> Start[Capture start]
    Start --> Work[Fixed selected work with active peer]
    Work --> End[Freeze end]
    End --> Cleanup[Stop and join peer]
```

The route records a separate `measurementWindow.Preparation` before the real start.
Preparation exercises the capture path without performing selected work or supplying measured progress.
Frozen progress deltas define measured work. Final peer totals include boundary-external work.
The route requires disabled tiering, ReadyToRun, and startup profiling.
Its managed implementation still requires execution checks for other platform and runtime combinations.
Baseline and optimized physical execution pass once each on Windows x64 and Linux x64 with 50,000 selected batches.
The physical parser accepts all four records, and workload input hashes remain unchanged.
The runtime versions differ. See [the retained evidence](../../reliability/features/native-signal/cooperative-physical.md).
The [first reviewed matched block](../../reliability/features/native-signal/cooperative-comparison.md) completes all four arms with unchanged inputs.
Both sampled records remain unqualified. The prediction interval is about -2.4% to +0.3%, against physical change of +14.1% to +69.8%.
Treatment freezes peer command 275 before cleanup. Frozen global delay remains 66,143,001 ns after later settlement.
`VerifyCooperativeComparison.ps1` retains a four-arm plan before preparation and initializes unexecuted outcomes.
It snapshots sources and binaries, compiles the cooperative native library from snapshots, and resolves image identities.
Each Linux container has read-only inputs and a separate writable arm directory.
Controlled runner checks cover successful collection, launch failure, nonzero exit, preflight failure, and changed inputs.
Original inputs are checked after capture. Changed retained hashes prevent analyzer execution.
The runner always returns failure while prediction qualification remains unmet.
The runner accepts opt-in `-ProbeInterval 8192`; its default remains 1024.
New plans declare `probeIntervalEvidenceVersion = 1` and require matching integer interval metadata in both sampled arms.
A missing or mismatched sampled value invalidates that arm and suppresses the comparison.
Legacy plans without the marker accept only 1024 and can lack record interval fields.
The output retains the parsed interval and nullable evidence version. This identifies the contract, not proof of collection success.
Physical arms remain uninstrumented. Payment bounds and qualification rules are unchanged.
The MiniScaffold targets are `CooperativeComparisonChecks` and `LinuxCooperativeComparison`.
`AnalyzeCooperativeComparison.py` parses process outcomes before aggregation and retains missing or failed arms.
It rejects duplicate JSON fields, Boolean counters, incomplete image digests, and missing required snapshots.
Portable checks cover real sampled-half replay and physical parsing. Collection orchestration checks use controlled arm results.
The analyzer suppresses comparisons after integrity errors. It never qualifies prediction accuracy.
It rejects zero sampled clock frequency before replay and retains per-arm arithmetic failures.
Outcomes retain the common container arguments as well as per-arm activation and workload arguments.
The first diagnostic block accepts cold boundary uncertainty. Its physical start brackets are about 108.6 ms and 56.1 ms.
The earlier physical-only capture has Linux start brackets of about 107 ms and 90 ms.
Source hashes do not bind source to prebuilt binaries. Direct physical failure paths still lack fault-injection checks.
These limits are accepted for an unqualified diagnostic capture, not for prediction or general failure-safety claims.
General comparison reliability and accuracy qualification remain incomplete.
Treatment has 143 selected pauses and 79,729 receipt-readiness calls.
The separate [phase capture](../../docsSrc/Explanations/cooperative-pause-cost.md) records 146 pauses and identifies callback intervals for further investigation.
Neither capture isolates observer cost or justifies changing sampled accounting.
The optimized arithmetic does not establish an exact 50% region-time reduction.
The shared checksum checks and counters change sampled per-batch costs relative to earlier captures.

See [the human reference](../../docsSrc/Reference/cooperative-physical-work.md),
[the comparison explanation](../../docsSrc/Explanations/cooperative-physical-comparison.md),
[finite measurement](cooperative-measurement.md), and [the index](../lode-map.md).
