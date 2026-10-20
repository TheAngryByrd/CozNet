namespace CozNet

open System
open System.IO
open System.Reflection
open System.Security.Cryptography
open System.Text

type TargetMapProvenance =
    | CapturedInProcess of processId: int * path: string
    | Supplied of label: string

type TargetMapHash =
    | LineContentSha256 of hex: string
    | FileBytesSha256 of hex: string

type PerfMapSnapshotError =
    | Unsupported
    | Unreadable of path: string * message: string
    | BlankLabel

type PerfMapSnapshot = private {
    SnapshotLines: string array
    SnapshotProvenance: TargetMapProvenance
    SnapshotHash: TargetMapHash
    SnapshotBytes: byte array option
} with
    member this.Lines = Array.copy this.SnapshotLines
    member this.Provenance = this.SnapshotProvenance
    member this.Hash = this.SnapshotHash
    member this.CopyCapturedBytes() = this.SnapshotBytes |> Option.map Array.copy

[<RequireQualifiedAccess>]
module PerfMapSnapshot =
    let private hex (bytes: byte array) = Convert.ToHexString(SHA256.HashData bytes)

    let internal linesOf (bytes: byte array) =
        let lines = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Split('\n') |> Array.map (fun line -> line.TrimEnd '\r')
        if lines.Length > 0 && lines[lines.Length - 1] = "" then Array.take (lines.Length - 1) lines else lines

    let internal ofCapturedBytes (processId: int) (path: string) (bytes: byte array) =
        let bytes = Array.copy bytes
        { SnapshotLines = linesOf bytes; SnapshotProvenance = CapturedInProcess(processId, path); SnapshotHash = FileBytesSha256(hex bytes); SnapshotBytes = Some bytes }

    let trySupplied (label: string) (lines: string seq) =
        if String.IsNullOrWhiteSpace label then
            Error BlankLabel
        else
            let lines = Seq.toArray lines
            Ok { SnapshotLines = lines; SnapshotProvenance = Supplied label
                 SnapshotHash = LineContentSha256(hex (Encoding.UTF8.GetBytes(String.Join("\n", lines)))); SnapshotBytes = None }

    let tryCaptureCurrentProcess () =
        if not (TargetLocalProfiler.isSupported ()) then
            Error Unsupported
        else
            let path = PerfMap.currentProcess ()

            try
                Ok(ofCapturedBytes Environment.ProcessId path (File.ReadAllBytes path))
            with
            | :? IOException
            | :? UnauthorizedAccessException as error -> Error(Unreadable(path, error.Message))

[<NoComparison>]
type TargetMethodCatalogError =
    | NoMethods
    | DuplicateSymbol of symbol: string * methods: MethodInfo array

[<NoComparison>]
type TargetMethodCatalog = private {
    CatalogMethods: Map<string, MethodInfo>
    CatalogUnsupported: (MethodInfo * TargetSymbolError) array
} with
    member this.Symbols = this.CatalogMethods |> Map.keys |> Seq.toArray
    member this.Unsupported = Array.copy this.CatalogUnsupported

[<RequireQualifiedAccess>]
module TargetMethodCatalog =
    let tryCreate (methods: MethodInfo seq) =
        let methods = Seq.toArray methods

        if methods.Length = 0 then
            Error NoMethods
        else
            let formatted = methods |> Array.map (fun m -> m, TargetSymbol.tryFormat m)
            let supported = formatted |> Array.choose (function m, Ok symbol -> Some(symbol, m) | _, Error _ -> None)
            let unsupported = formatted |> Array.choose (function m, Error error -> Some(m, error) | _, Ok _ -> None)

            match supported |> Array.groupBy fst |> Array.tryFind (fun (_, group) -> group.Length > 1) with
            | Some(symbol, group) -> Error(DuplicateSymbol(symbol, group |> Array.map snd))
            | None -> Ok { CatalogMethods = Map.ofArray supported; CatalogUnsupported = unsupported }

    let internal tryFind (catalog: TargetMethodCatalog) (symbol: string) = catalog.CatalogMethods.TryFind symbol

type TargetCandidateEligibility =
    | InEligibleBinding
    | OutsideEligibleBinding

type TargetAnchorResult =
    | Anchored of TargetSourceAnchor
    | NoCatalog
    | NotInCatalog
    | CatalogAssemblyDiffers of catalogAssembly: string
    | AnchorFailed of TargetSourceAnchorError

type TargetDiscoveredCandidate = {
    Method: DiscoveredMethod
    Eligibility: TargetCandidateEligibility
    Anchor: TargetAnchorResult
}

type TargetDiscoveryLimitation =
    | SpeedupNotRecorded
    | RunBindingIsCallerPrecondition
    | RunMapAssociationNotProven
    | MapTimingNotProven
    | MapSupplied of label: string
    | MethodLevelOnly
    | NoCallchain

type TargetDiscoveryWorkflowError =
    | MapMalformed of PerfMapIndexError
    | BindingDisagreesWithMap of message: string
    | Ranking of TargetDiscoveryError

type TargetDiscoveryDesignError =
    | NotDiscovered of symbol: string
    | EligibleNotBound of message: string
    | Design of TargetCausalDesignError

type TargetDiscoveryOutcome = private {
    OutcomeSnapshot: PerfMapSnapshot
    OutcomeCounts: TargetDiscoveryCounts
    OutcomeCandidates: TargetDiscoveredCandidate array
    OutcomeOmittedMethods: int
    OutcomeOmittedSamples: int64
    OutcomeLimitations: TargetDiscoveryLimitation list
} with
    member this.Snapshot = this.OutcomeSnapshot
    member this.Counts = this.OutcomeCounts

    member this.Candidates =
        this.OutcomeCandidates |> Array.map (fun c -> { c with Method = { c.Method with WorkerSamples = Array.copy c.Method.WorkerSamples } })

    member this.OmittedMethods = this.OutcomeOmittedMethods
    member this.OmittedSamples = this.OutcomeOmittedSamples
    member this.Limitations = this.OutcomeLimitations

[<RequireQualifiedAccess>]
module TargetDiscoveryWorkflow =
    let private limitations (snapshot: PerfMapSnapshot) =
        [ SpeedupNotRecorded
          RunBindingIsCallerPrecondition
          RunMapAssociationNotProven
          MapTimingNotProven
          match snapshot.Provenance with
          | Supplied label -> MapSupplied label
          | CapturedInProcess _ -> ()
          MethodLevelOnly
          NoCallchain ]

    let private anchor (catalog: TargetMethodCatalog option) (found: DiscoveredMethod) =
        match catalog with
        | None -> NoCatalog
        | Some catalog ->
            match TargetMethodCatalog.tryFind catalog found.Symbol with
            | None -> NotInCatalog
            | Some method when not (String.Equals(method.Module.Assembly.GetName().Name, found.Assembly, StringComparison.Ordinal)) ->
                CatalogAssemblyDiffers(method.Module.Assembly.GetName().Name)
            | Some method ->
                match TargetSourceAnchor.tryFind method with
                | Ok anchor -> Anchored anchor
                | Error error -> AnchorFailed error

    let prepare (scope: TargetDiscoveryScope) (eligible: CodeBinding) (run: TargetProfileResult) (map: PerfMapSnapshot)
        (catalog: TargetMethodCatalog option) (limit: TargetDiscoveryLimit) =
        let lines = map.SnapshotLines

        match PerfMapIndex.tryCreate lines with
        | Error error -> Error(MapMalformed error)
        | Ok index ->
            match CodeBinding.verify lines eligible with
            | Error message -> Error(BindingDisagreesWithMap message)
            | Ok() ->
                match TargetDiscovery.rank scope index run limit with
                | Error error -> Error(Ranking error)
                | Ok result ->
                    let ranges = eligible.Ranges |> Set.ofArray
                    let candidates =
                        result.Ranked
                        |> Array.map (fun m ->
                            { Method = { m with WorkerSamples = Array.copy m.WorkerSamples }
                              Eligibility = if ranges.Contains(m.Start, m.End) then InEligibleBinding else OutsideEligibleBinding
                              Anchor = anchor catalog m })
                    Ok { OutcomeSnapshot = map; OutcomeCounts = result.Counts; OutcomeCandidates = candidates
                         OutcomeOmittedMethods = result.OmittedMethods; OutcomeOmittedSamples = result.OmittedSamples; OutcomeLimitations = limitations map }

    let tryDesign (outcome: TargetDiscoveryOutcome) (eligible: string array) (candidates: string array) (period: uint64) (speedups: uint32 array)
        (blocks: int) (seed: int) (rotation: int) (progress: TargetProgressPoint) =
        let discovered = outcome.OutcomeCandidates |> Array.map _.Method.Symbol |> Set.ofArray

        match candidates |> Array.filter (discovered.Contains >> not) |> Array.distinct |> Array.toList with
        | _ :: _ as missing -> Error(missing |> List.map NotDiscovered)
        | [] ->
            match CodeBinding.tryCapture outcome.OutcomeSnapshot.SnapshotLines eligible with
            | Error message -> Error [ EligibleNotBound message ]
            | Ok binding ->
                TargetCausalDesign.tryCreate binding period eligible candidates speedups blocks seed rotation progress
                |> Result.mapError (List.map Design)
