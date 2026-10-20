# Target-local discovery workflow

Status: proposed on 2026-10-19. It needs supervisor approval before any implementation. No code exists for it.

## Gap

The only discovery-to-design path is `samples/CozNet.TargetLocal/DiscoveryCheck.fs`. It is sample code, and it has three problems for a library user:
- It passes the ranked symbols as both the eligible and the candidate methods of `TargetCausalDesign.tryCreate`. So every discovered method becomes a payment-eligible range without a decision by the caller.
- It scans every method of one assembly for anchors, and it builds the anchor map itself.
- It takes the perf map as plain lines, so the result does not say where the map came from.

The [discovery check](target-local-discovery-check.md) validated that sample path once, for two methods. It does not validate a library workflow.

## What the workflow can enforce, and what stays a caller precondition

| Property | Enforced by the workflow | Caller precondition |
| --- | --- | --- |
| The run completed, with no failure code, lost sample, or replay error | Yes, through `TargetDiscovery.rank` | |
| The run added no delay | Yes: `GlobalNs` is 0 and no sample has credit | |
| The run used speedup 0 | No. The result does not record the speedup. | The caller ran at 0%. |
| The run used the given eligible binding | No. The result does not record its binding. | The caller passes the binding of the run. |
| The map agrees with the binding | Yes: `CodeBinding.verify` of the binding against the map | |
| The map came from the run process, after the run | Only when the map was captured by `PerfMapSnapshot.tryCaptureCurrentProcess`, which records the process id. A supplied map is labeled as supplied. | The caller captures it in the run process. |
| Each map entry existed at each sample time | No | |
| A ranked method is inside the eligible binding | Yes, as a property of each candidate | |

The workflow does not return a `verified` flag. It returns the enforced checks as errors, and the preconditions as an explicit list of limitations in each report.

## Eligibility

A discovered method outside the eligible binding is descriptive only. The workflow marks it `OutsideEligibleBinding` and never adds its range to a payment set.
- The design function takes the eligible methods and the candidates as two separate lists from the caller.
- `TargetCausalDesign.tryCreate` keeps its rule that each candidate must be eligible. So a candidate outside the eligible list gives the existing `IneligibleCandidate` error.
- To profile such a method, the caller must put it in the eligible list. The design then binds the eligible list again over the same map.
- A later run binds and verifies again against its own map, because `TargetCausalProfile.run` checks the binding before and after the run. A stale binding then fails the run. It is not admitted.

## Proposed API

A new file `src/CozNet/TargetDiscoveryWorkflow.fs`, after `TargetDiscovery.fs` in `src/CozNet/CozNet.fsproj`. It reuses `PerfMapIndex`, `TargetDiscoveryScope`, `TargetDiscoveryLimit`, `TargetDiscovery.rank`, `TargetSymbol`, `TargetSourceAnchor`, `CodeBinding`, and `TargetCausalDesign`, and it copies none of their rules.

```fsharp
type TargetMapProvenance =
    | CapturedInProcess of processId: int * path: string
    | Supplied of label: string

type PerfMapSnapshot = private { SnapshotLines: string array; SnapshotProvenance: TargetMapProvenance; SnapshotSha256: string }
// members: Lines, Provenance, Sha256

module PerfMapSnapshot =
    /// Linux x64 only. Reads /tmp/perf-<pid>.map of the current process. Otherwise Error Unsupported.
    val tryCaptureCurrentProcess: unit -> Result<PerfMapSnapshot, PerfMapSnapshotError>
    /// For synthetic or replayed lines. The label must name the source, for example "replayed: <run folder>".
    val supplied: label: string -> lines: string seq -> Result<PerfMapSnapshot, PerfMapSnapshotError>

type TargetMethodCatalog = private { CatalogMethods: Map<string, MethodInfo>; CatalogUnsupported: (MethodInfo * TargetSymbolError) array }

module TargetMethodCatalog =
    /// Formats each given method. Two methods with one symbol give DuplicateSymbol. No assembly scan by default.
    val tryCreate: methods: MethodInfo seq -> Result<TargetMethodCatalog, TargetMethodCatalogError>

type TargetDiscoveryRequest = private { ... }

module TargetDiscoveryRequest =
    val tryCreate:
        scope: TargetDiscoveryScope -> eligible: CodeBinding -> run: TargetProfileResult -> map: PerfMapSnapshot
        -> catalog: TargetMethodCatalog option -> limit: TargetDiscoveryLimit -> Result<TargetDiscoveryRequest, TargetDiscoveryRequestError list>
    // errors: MapMalformed of PerfMapIndexError | BindingDisagreesWithMap of message

type TargetCandidateEligibility = InEligibleBinding | OutsideEligibleBinding

type TargetAnchorResult =
    | Anchored of TargetSourceAnchor
    | NoCatalog
    | NotInCatalog
    | AnchorFailed of TargetSourceAnchorError

type TargetDiscoveredCandidate = { Method: DiscoveredMethod; Eligibility: TargetCandidateEligibility; Anchor: TargetAnchorResult }

type TargetDiscoveryLimitation =
    | SpeedupNotRecorded
    | RunBindingIsCallerPrecondition
    | MapTimingNotProven
    | MapSupplied of label: string
    | MethodLevelOnly
    | NoCallchain

type TargetDiscoveryReport = {
    Provenance: TargetMapProvenance
    MapSha256: string
    Counts: TargetDiscoveryCounts
    Candidates: TargetDiscoveredCandidate array
    OmittedMethods: int
    OmittedSamples: int64
    Limitations: TargetDiscoveryLimitation list
}

module TargetDiscoveryWorkflow =
    /// Ranks with TargetDiscovery.rank. Errors are the existing TargetDiscoveryError cases.
    val run: request: TargetDiscoveryRequest -> Result<TargetDiscoveryReport, TargetDiscoveryError>
    /// Every candidate must be a ranked symbol of the report. It binds the eligible list over the map and calls TargetCausalDesign.tryCreate.
    val tryDesign:
        report: TargetDiscoveryReport -> map: PerfMapSnapshot -> eligible: string array -> candidates: string array
        -> period: uint64 -> speedups: uint32 array -> blocks: int -> seed: int -> rotation: int -> progress: TargetProgressPoint
        -> Result<TargetCausalDesign, TargetDiscoveryDesignError list>
    // errors: NotDiscovered of symbol | EligibleNotBound of message | Design of TargetCausalDesignError
```

- A missing catalog, a symbol outside the catalog, or a failed anchor gives a per-candidate anchor result. It never removes the candidate or its samples.
- The report has no field that joins two code versions. Each candidate is one `DiscoveredMethod`, one entry of the map.
- `tryCaptureCurrentProcess` is the only Linux part. It checks `TargetLocalProfiler.isSupported`, so on another platform it gives `Unsupported`, and the rest of the file stays portable.

## Call site

The sample would replace its own ranking, anchor, binding, and design code with this:

```fsharp
let map = PerfMapSnapshot.tryCaptureCurrentProcess () |> Result.defaultWith (sprintf "%A" >> failwith)
let catalog = TargetMethodCatalog.tryCreate (workModule.GetMethods()) |> Result.toOption
let report =
    TargetDiscoveryRequest.tryCreate scope binding result map catalog limit
    |> Result.mapError (sprintf "%A")
    |> Result.bind (TargetDiscoveryWorkflow.run >> Result.mapError (sprintf "%A"))
let design =
    report
    |> Result.bind (fun report ->
        let chosen = report.Candidates |> Array.filter (fun c -> c.Eligibility = InEligibleBinding) |> Array.map _.Method.Symbol
        TargetDiscoveryWorkflow.tryDesign report map binding.Symbols chosen period [| 50u |] 1 0 0 EndToEnd |> Result.mapError (sprintf "%A"))
```

`workModule` is a type that the caller names. The workflow does not search assemblies.

## Acceptance criteria

Proposed. They would add to criteria 1 to 186 and change none.

187. The checks extend `tests/CozNet.Tests/TargetDiscoveryTests.fs` and reuse its real labels, its `classEvents`, and its `run ()` record. They do not repeat the class, permutation, limit, and failure-field checks of `rank`. One incomplete run and one run with delay show that the workflow passes `rank` errors through.
188. A report from `classEvents` keeps the mixed methods, the unmapped and out-of-scope samples, and the two generic code versions as `NotUnique`. Its counts equal those of `rankEventsUnchecked`.
189. A binding whose ranges differ from the map gives `BindingDisagreesWithMap`. A malformed map gives `MapMalformed`.
190. A ranked method outside the eligible binding is `OutsideEligibleBinding`. `tryDesign` rejects it as a candidate with `IneligibleCandidate`, and accepts it after the caller adds it to the eligible list.
191. `tryDesign` rejects a candidate that is not in the report with `NotDiscovered`. The eligible list and the candidate list stay separate in the result design.
192. No catalog gives `NoCatalog`, a symbol outside the catalog gives `NotInCatalog`, and a copy of an assembly without its PDB gives `AnchorFailed`. In each case, the candidate and its samples stay in the report.
193. A catalog with two methods of one symbol gives `DuplicateSymbol`. A catalog keeps the methods that it cannot format as a list, and it does not fail for them.
194. With a limit below the method count, the report gives `OmittedMethods` and `OmittedSamples`, and the in-scope count equals the candidate samples plus the omitted samples.
195. A supplied map has `Supplied` provenance and the limitation `MapSupplied`. Every report has `SpeedupNotRecorded`, `RunBindingIsCallerPrecondition`, `MapTimingNotProven`, `MethodLevelOnly`, and `NoCallchain`.
196. On a platform other than Linux x64, `tryCaptureCurrentProcess` gives `Unsupported`. The check is skipped on Linux.
197. The sample mode `discovery-check` uses the workflow. Its record keeps its fields, so `native/AnalyzeTargetDiscovery.py` and its checks do not change.
198. No live capture runs in this milestone.

## Inputs and labels

- Every test input is synthetic. Its map is built with `PerfMapSnapshot.supplied` and a label that starts with `synthetic:`.
- A replayed input would use a label that starts with `replayed:` and names its run folder. The retained live capture holds the map and the counts, but not the sample events, so it cannot be replayed through `run`.

## What the earlier live capture does not validate

The capture `20261020T015420785-8c8adf21` validated the sample orchestration for two methods. It does not validate:
- `tryCaptureCurrentProcess` and the provenance of a captured map.
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
