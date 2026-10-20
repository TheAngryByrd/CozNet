# Target-local discovery workflow

Status: approved on 2026-10-19 with the changes below, and implemented in `src/CozNet/TargetDiscoveryWorkflow.fs`. No live run has used it.

## Gap

This section gives the state before the implementation.

The only discovery-to-design path was `samples/CozNet.TargetLocal/DiscoveryCheck.fs`. It was sample code, and it had three problems for a library user:
- It passed the ranked symbols as both the eligible and the candidate methods of `TargetCausalDesign.tryCreate`. So every discovered method became a payment-eligible range without a decision by the caller.
- It scanned every method of one assembly for anchors, and it built the anchor map itself.
- It took the perf map as plain lines, so the result did not say where the map came from.

The [discovery check](target-local-discovery-check.md) validated that sample path once, for two methods. It does not validate the library workflow.

## Decision

The supervisor approved the milestone on 2026-10-19 with these changes, which this revision of the plan incorporates before the implementation:
- One opaque result binds the ranking to its own map snapshot. `tryDesign` takes only that result, so a candidate of map A cannot be bound again over map B.
- The snapshot copies its lines, and no accessor exposes a mutable backing array. The plan defines the two hash forms exactly.
- `CapturedInProcess` proves only that the current process read the map. It does not prove that the run came from that process, or that the run came before the read.
- One parse boundary, `prepare`. There is no request type that only stores unchecked data.
- No live capture and no experiment with a speedup above 0.

## What the workflow can enforce, and what stays a caller precondition

| Property | Enforced by the workflow | Caller precondition |
| --- | --- | --- |
| The run completed, with no failure code, lost sample, or replay error | Yes, through `TargetDiscovery.rank` | |
| The run added no delay | Yes: `GlobalNs` is 0 and no sample has credit | |
| The run used speedup 0 | No. The result does not record the speedup. | The caller ran at 0%. |
| The run used the given eligible binding | No. The result does not record its binding. | The caller passes the binding of the run. |
| The eligible binding agrees with the map | Yes: `CodeBinding.verify` of the binding against the snapshot | |
| The run and the map belong to one process, and the map was read after the run | No. A captured snapshot records only that the current process read the map. | The caller runs and captures in one process, in that order. |
| Each map entry existed at each sample time | No | |
| A ranked method is inside the eligible binding | Yes, as a property of each candidate | |
| A design uses the ranges of its own ranking | Yes: the result holds its snapshot, and `tryDesign` binds over that snapshot only | |

The workflow does not return a `verified` flag. It returns the enforced checks as errors, and each report holds the limitations below.

## Eligibility

A discovered method outside the eligible binding is descriptive only. The workflow marks it `OutsideEligibleBinding` and never adds its range to a payment set.
- `tryDesign` takes the eligible methods and the candidates as two separate lists from the caller.
- `TargetCausalDesign.tryCreate` keeps its rule that each candidate must be eligible. So a candidate outside the eligible list gives the existing `UnboundCandidate` and `IneligibleCandidate` errors.
- To profile such a method, the caller must put it in the eligible list. `tryDesign` then binds the whole eligible list over the snapshot of the result.
- A later run checks its binding against its own map, by symbol and range. That check does not prove that a range belongs to the same code, because the map records no code identity and an address can be reused.

## Hashes

- `LineContentSha256`: the SHA-256 of the UTF-8 bytes of the lines joined with `\n`, without a final `\n`. A supplied snapshot has this hash.
- `FileBytesSha256`: the SHA-256 of the raw bytes of the map file. A captured snapshot has this hash.
- A capture reads the file once. It derives the lines from those bytes: it decodes them as UTF-8, removes a leading byte-order mark, splits them at `\n`, removes a final `\r` from each line, and drops one empty last line. A lone `\r` does not split a line. The runtime writes neither form.
- A captured snapshot keeps its bytes. `CopyCapturedBytes` returns a copy, so a caller can retain the exact file.

## API

`src/CozNet/TargetDiscoveryWorkflow.fs`, after `TargetDiscovery.fs` in `src/CozNet/CozNet.fsproj`. It reuses `PerfMapIndex`, `TargetDiscoveryScope`, `TargetDiscoveryLimit`, `TargetDiscovery.rank`, `TargetSymbol`, `TargetSourceAnchor`, `CodeBinding`, and `TargetCausalDesign`, and it copies none of their rules.

```fsharp
type TargetMapProvenance =
    | CapturedInProcess of processId: int * path: string
    | Supplied of label: string

type TargetMapHash =
    | LineContentSha256 of hex: string
    | FileBytesSha256 of hex: string

type PerfMapSnapshot  // opaque: Lines (a copy), Provenance, Hash, CopyCapturedBytes ()

module PerfMapSnapshot =
    val tryCaptureCurrentProcess: unit -> Result<PerfMapSnapshot, PerfMapSnapshotError>  // Unsupported | Unreadable
    val trySupplied: label: string -> lines: string seq -> Result<PerfMapSnapshot, PerfMapSnapshotError>  // BlankLabel

type TargetMethodCatalog  // opaque: Symbols (a copy), Unsupported (a copy of (MethodInfo * TargetSymbolError) pairs)

module TargetMethodCatalog =
    val tryCreate: methods: MethodInfo seq -> Result<TargetMethodCatalog, TargetMethodCatalogError>  // NoMethods | DuplicateSymbol

type TargetCandidateEligibility = InEligibleBinding | OutsideEligibleBinding

type TargetAnchorResult = Anchored of TargetSourceAnchor | NoCatalog | NotInCatalog | CatalogAssemblyDiffers of catalogAssembly: string | AnchorFailed of TargetSourceAnchorError

type TargetDiscoveredCandidate = { Method: DiscoveredMethod; Eligibility: TargetCandidateEligibility; Anchor: TargetAnchorResult }

type TargetDiscoveryLimitation =
    | SpeedupNotRecorded
    | RunBindingIsCallerPrecondition
    | RunMapAssociationNotProven
    | MapTimingNotProven
    | MapSupplied of label: string
    | MethodLevelOnly
    | NoCallchain

type TargetDiscoveryOutcome  // opaque: Snapshot, Counts, Candidates (copies), OmittedMethods, OmittedSamples, Limitations

type TargetDiscoveryWorkflowError =
    | MapMalformed of PerfMapIndexError
    | BindingDisagreesWithMap of message: string
    | Ranking of TargetDiscoveryError

type TargetDiscoveryDesignError =
    | NotDiscovered of symbol: string
    | EligibleNotBound of message: string
    | Design of TargetCausalDesignError

module TargetDiscoveryWorkflow =
    val prepare:
        scope: TargetDiscoveryScope -> eligible: CodeBinding -> run: TargetProfileResult -> map: PerfMapSnapshot
        -> catalog: TargetMethodCatalog option -> limit: TargetDiscoveryLimit -> Result<TargetDiscoveryOutcome, TargetDiscoveryWorkflowError>
    val tryDesign:
        outcome: TargetDiscoveryOutcome -> eligible: string array -> candidates: string array
        -> period: uint64 -> speedups: uint32 array -> blocks: int -> seed: int -> rotation: int -> progress: TargetProgressPoint
        -> Result<TargetCausalDesign, TargetDiscoveryDesignError list>
```

- `prepare` ranks the run at once and keeps no reference to the run or its events.
- `TargetDiscoveredCandidate` is display data. `tryDesign` takes symbols and looks them up in the outcome, so a record that a caller builds cannot add a candidate.
- `Candidates` returns new records with copied arrays.
- A missing catalog is `None`. A catalog error comes from `TargetMethodCatalog.tryCreate`, and the caller must handle it. The methods that a catalog cannot format stay visible in `Unsupported`.
- A catalog method anchors a candidate only when its assembly name equals the assembly of the candidate label. Otherwise the result is `CatalogAssemblyDiffers`, because two assemblies can hold one canonical symbol.
  The check uses the simple assembly name. That catalog methods come from the same build as the profiled code is a caller precondition. Review round 2 added this case.
- The catalog and its error type are `NoComparison`, because they hold `MethodInfo` values.
- `tryCaptureCurrentProcess` is the only Linux part. It checks `TargetLocalProfiler.isSupported`, and on another platform it gives `Unsupported`.

## Call site

```fsharp
match PerfMapSnapshot.tryCaptureCurrentProcess (), TargetMethodCatalog.tryCreate [ selectedMethod; peerMethod ] with
| Ok map, Ok catalog ->
    match TargetDiscoveryWorkflow.prepare scope binding result map (Some catalog) limit with
    | Ok outcome ->
        let candidates = outcome.Candidates |> Array.filter (fun c -> c.Eligibility = InEligibleBinding) |> Array.map _.Method.Symbol
        TargetDiscoveryWorkflow.tryDesign outcome binding.Symbols candidates period [| 50u |] 1 0 0 EndToEnd
    | Error error -> ...
| Error error, _ -> ...
| _, Error error -> ...
```

`selectedMethod` and `peerMethod` are the `MethodInfo` values of the known work methods. The workflow does not search assemblies, and the caller does not call `GetMethods()` with inherited members.

## Sample record

The sample mode `discovery-check` uses the workflow. Its record keeps its fields. These meanings change, and the analyzer and its fixtures change with them:
- `mapSha256` is now the hash of the raw file bytes, and `discovery.json.perf-map` holds those bytes. Before, both were the joined lines. The analyzer check is the same: the retained file must have the recorded hash.
- Each ranked entry gets `eligibility`. The analyzer requires both fixture methods to be `InEligibleBinding`.
- `boundRanges` stays the binding of all ranked symbols over the snapshot.
- `designValid` is now the design with the eligible binding as the eligible list and the in-binding candidates. Before, every ranked symbol was both eligible and a candidate.
- A new field `outsideBinding` lists the ranked symbols outside the eligible binding. The analyzer requires it to equal the ranked entries marked `OutsideEligibleBinding`.
- New fields `designEligible` and `designCandidates` give the lists of the design. The analyzer requires the eligible list of the record and the in-binding candidates, so a return to the earlier design, with every ranked symbol eligible, fails.
- A capture error or a catalog error is in `mapError` or `catalogError`, and the analyzer reports it.

The retained capture `20261020T015420785-8c8adf21` ran the earlier orchestration. It does not validate the new one.

## Acceptance criteria

These criteria were revised after the approval and before the implementation. They add to criteria 1 to 186 and change none.

187. The checks extend `tests/CozNet.Tests/TargetDiscoveryTests.fs` and reuse its real labels, its `classEvents`, and its `run ()` record. They do not repeat the class, permutation, limit, and failure-field checks of `rank`. One incomplete run and one run with delay show that `prepare` passes `rank` errors through.
188. An outcome from `classEvents` keeps the mixed methods, the unmapped and out-of-scope samples, and the two generic code versions as `NotUnique`. Its counts equal those of `rankEventsUnchecked`.
189. A binding whose ranges differ from the snapshot gives `BindingDisagreesWithMap`. A malformed snapshot gives `MapMalformed`.
190. Map A and map B hold one symbol at different ranges. An outcome of map A gives a design with the range of map A. The binding of map A against map B gives `BindingDisagreesWithMap`.
191. A change of the caller's line array or event array after `trySupplied` or `prepare` does not change the snapshot, its hash, or the outcome. A change of an array that an accessor returned does not change the snapshot or the outcome.
192. A ranked method outside the eligible binding is `OutsideEligibleBinding`. `tryDesign` rejects it as a candidate with `UnboundCandidate` and `IneligibleCandidate` until the caller adds it to the eligible list, and then accepts it.
193. `tryDesign` rejects a symbol that is not in the outcome with `NotDiscovered`, and an eligible list that does not bind with `EligibleNotBound`. A display record that a caller builds cannot add a candidate, because `tryDesign` takes only symbols and an outcome.
194. No catalog gives `NoCatalog`, a symbol outside the catalog gives `NotInCatalog`, a catalog method of another assembly gives `CatalogAssemblyDiffers`, and a method of a copied assembly without its PDB or of a dynamic assembly gives `AnchorFailed`. In each case, the candidate and its samples stay in the outcome. Review round 1 added the other assembly and the copied assembly.
195. A catalog with two methods of one symbol gives `DuplicateSymbol`, and an empty catalog gives `NoMethods`. A method that the catalog cannot format stays in `Unsupported`.
196. With a limit below the method count, the outcome gives `OmittedMethods` and `OmittedSamples`, and the in-scope count equals the candidate samples plus the omitted samples.
197. A supplied snapshot has `Supplied` provenance, a `LineContentSha256` hash of the defined form, and the limitation `MapSupplied`. Every outcome has `SpeedupNotRecorded`, `RunBindingIsCallerPrecondition`, `RunMapAssociationNotProven`, `MapTimingNotProven`, `MethodLevelOnly`, and `NoCallchain`.
198. On a platform other than Linux x64, `tryCaptureCurrentProcess` gives `Unsupported`. The check is skipped on Linux. The line split of captured bytes is checked on fixed bytes.
199. The sample uses the workflow. The analyzer and its fixtures check the changed field meanings above.
200. No live capture runs in this milestone.

## Inputs and labels

- Every test input is synthetic. Its snapshot is built with `PerfMapSnapshot.trySupplied` and a label that starts with `synthetic:`.
- A replayed input would use a label that starts with `replayed:` and names its run folder. The retained live capture holds the map and the counts, but not the sample events, so it cannot be replayed through `prepare`.

## What the earlier live capture does not validate

The capture `20261020T015420785-8c8adf21` validated the earlier sample orchestration for two methods. It does not validate:
- `tryCaptureCurrentProcess`, its byte hash, or its line split.
- The check of the binding against the map.
- A catalog that the caller supplies, and the anchor results other than `Anchored`.
- Any candidate outside the eligible binding, because both live candidates were eligible.
- `tryDesign` with separate eligible and candidate lists.
- Any of the three classes other than in scope.

A live run of the new workflow would need its own approval.

## What this leaves open

- Every qualification blocker.
- Discovery for an arbitrary application, which still needs a caller-supplied scope, eligible binding, and zero-delay run.
- Sub-method attribution, callchains, and symbol forms outside the verified subset.

See [target-local discovery](target-local-discovery.md) and [the discovery check](target-local-discovery-check.md).
