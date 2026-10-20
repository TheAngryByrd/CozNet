namespace CozNet

open System

type TargetCodeRegion = { Start: uint64; Size: uint64 }

type TargetCodeEvidence = {
    Label: string
    ModuleVersionId: Guid
    MethodToken: int
    CodeStart: uint64
    Regions: TargetCodeRegion list
    NativeBodyCount: int
    Entries: struct (uint32 * uint32 * uint32) array
}

type TargetLinePrepareError =
    | CandidateNotDiscovered of symbol: string
    | EvidenceLabelBlank
    | ColdCodeUnsupported of regions: int
    | RangeMismatch of discovered: (uint64 * uint64) * evidence: (uint64 * uint64)
    | RangeOverflow
    | MultipleBodiesUnsupported of bodies: int
    | IdentityMismatch of field: string * expected: string * actual: string
    | SharedGenericUnsupported
    | NoIlBody
    | SourceError of TargetSourceAnchorError
    | EmptyMap
    | InvertedRange of index: int
    | IlOffsetBeyondBody of index: int * raw: uint32
    | UnsupportedMapShape of reason: string

type TargetLineLimitation =
    | SuppliedEvidence of label: string
    | OptimizedBoundariesAreCoarse
    | SequencePointRangeInferred
    | InlineesAttributedToCallSite
    | IlRewriteNotExcluded
    | MapEntryTimingNotProven
    | CodeLifetimeNotProven
    | NoCallchainAttribution

type TargetLineLocation = {
    Document: string
    StartLine: int
    StartColumn: int
    EndLine: int
    EndColumn: int
    IlStart: int
    IlEnd: int
}

type TargetLineResult =
    | Line of TargetLineLocation
    | InProlog
    | InEpilog
    | Unmapped
    | Uncovered
    | HiddenSequencePoint of ilOffset: uint32
    | NoSequencePoint of ilOffset: uint32
    | OutsideBody

type internal TargetMapTarget =
    | MapIl of uint32
    | MapProlog
    | MapEpilog
    | MapNone

type TargetLineResolver = private {
    ResolverLabel: string
    ResolverSymbol: string
    ResolverStart: uint64
    ResolverSize: uint64
    ResolverEntries: struct (uint32 * uint32 * TargetMapTarget) array
    ResolverUncovered: (uint64 * uint64) array
    ResolverPoints: TargetSourceAnchor.Point array
    ResolverIlBodySize: int
    ResolverLimitations: TargetLineLimitation list
} with
    member this.Label = this.ResolverLabel
    member this.Symbol = this.ResolverSymbol
    member this.CodeRange = this.ResolverStart, this.ResolverStart + this.ResolverSize
    member this.Uncovered = Array.copy this.ResolverUncovered
    member this.Limitations = this.ResolverLimitations

[<RequireQualifiedAccess>]
module TargetLineAttribution =
    let private noMapping = 0xFFFFFFFFu
    let private prolog = 0xFFFFFFFEu
    let private epilog = 0xFFFFFFFDu

    let private limitations label =
        [ SuppliedEvidence label; OptimizedBoundariesAreCoarse; SequencePointRangeInferred; InlineesAttributedToCallSite
          IlRewriteNotExcluded; MapEntryTimingNotProven; CodeLifetimeNotProven; NoCallchainAttribution ]

    let private evidenceErrors (candidate: DiscoveredMethod) (evidence: TargetCodeEvidence) =
        [ if String.IsNullOrWhiteSpace evidence.Label then EvidenceLabelBlank
          if evidence.NativeBodyCount <> 1 then MultipleBodiesUnsupported evidence.NativeBodyCount
          match evidence.Regions with
          | [ region ] ->
              if region.Size = 0UL || region.Size > uint64 UInt32.MaxValue || evidence.CodeStart > UInt64.MaxValue - region.Size then RangeOverflow
              elif region.Start <> evidence.CodeStart || (candidate.Start, candidate.End) <> (evidence.CodeStart, evidence.CodeStart + region.Size) then
                  RangeMismatch((candidate.Start, candidate.End), (region.Start, region.Start + region.Size))
          | regions -> ColdCodeUnsupported regions.Length ]

    let private identityErrors (candidate: DiscoveredMethod) (evidence: TargetCodeEvidence) (record: TargetSourceAnchor.MethodRecord) =
        let split = candidate.Symbol.IndexOf("::", StringComparison.Ordinal)
        let typeName = if split < 0 then "" else candidate.Symbol.Substring(0, split)
        let rest = if split < 0 then "" else candidate.Symbol.Substring(split + 2)
        let methodName, parameters =
            match rest.IndexOf '(' with
            | -1 -> rest, None
            | index when rest.EndsWith ')' -> rest.Substring(0, index), Some(rest.Substring(index + 1, rest.Length - index - 2))
            | index -> rest.Substring(0, index), None
        [ if record.ModuleVersionId <> evidence.ModuleVersionId then
              IdentityMismatch("module version id", string evidence.ModuleVersionId, string record.ModuleVersionId)
          if not (String.Equals(record.AssemblyName, candidate.Assembly, StringComparison.Ordinal)) then
              IdentityMismatch("assembly", candidate.Assembly, record.AssemblyName)
          if not (String.Equals(record.TypeName, typeName, StringComparison.Ordinal) && String.Equals(record.MethodName, methodName, StringComparison.Ordinal)) then
              IdentityMismatch("method", $"{typeName}::{methodName}", $"{record.TypeName}::{record.MethodName}")
          if record.Parameters.Contains(TargetSourceAnchor.unsupportedType, StringComparison.Ordinal) || parameters <> Some record.Parameters then
              IdentityMismatch("signature", defaultArg parameters "", record.Parameters)
          if record.Generic then SharedGenericUnsupported
          if record.IlBodySize.IsNone then NoIlBody ]

    let private decode (ilSize: int) (size: uint64) (entries: struct (uint32 * uint32 * uint32) array) =
        let decoded =
            entries
            |> Array.mapi (fun index (struct (il, start, finish)) ->
                let target =
                    if il = noMapping then Ok MapNone
                    elif il = prolog then Ok MapProlog
                    elif il = epilog then Ok MapEpilog
                    elif il < uint32 ilSize then Ok(MapIl il)
                    else Error(IlOffsetBeyondBody(index, il))
                match target with
                | Error error -> Error error
                | Ok _ when start > finish -> Error(InvertedRange index)
                | Ok _ when uint64 finish > size -> Error(UnsupportedMapShape $"entry {index} ends at {finish}, above the code size {size}")
                | Ok target -> Ok(struct (start, finish, target)))

        match decoded |> Array.choose (function Error error -> Some error | Ok _ -> None) |> Array.toList with
        | _ :: _ as errors -> Error errors
        | [] when entries.Length = 0 -> Error [ EmptyMap ]
        | [] ->
            let sized =
                decoded
                |> Array.choose (function Ok(struct (s, f, t)) when f > s -> Some(struct (s, f, t)) | _ -> None)
                |> Array.sortBy (fun (struct (s, f, _)) -> s, f)

            match sized |> Array.pairwise |> Array.tryFind (fun (struct (_, f, _), struct (s, _, _)) -> s < f) with
            | Some(struct (s1, f1, _), struct (s2, f2, _)) -> Error [ UnsupportedMapShape $"entries [{s1}, {f1}) and [{s2}, {f2}) overlap" ]
            | None ->
                let uncovered =
                    let edges = Array.append (sized |> Array.map (fun (struct (s, f, _)) -> s, f)) [| uint32 size, uint32 size |]
                    edges
                    |> Array.fold (fun (position, gaps) (s, f) -> max position f, (if s > position then (position, s) :: gaps else gaps)) (0u, [])
                    |> snd
                    |> List.rev
                    |> List.toArray
                Ok(sized, uncovered)

    let tryPrepare (outcome: TargetDiscoveryOutcome) (symbol: string) (assemblyPath: string) (evidence: TargetCodeEvidence) =
        match outcome.Candidates |> Array.tryFind (fun c -> String.Equals(c.Method.Symbol, symbol, StringComparison.Ordinal)) with
        | None -> Error [ CandidateNotDiscovered symbol ]
        | Some candidate ->
            let candidate = candidate.Method
            let entries = Array.copy evidence.Entries

            match evidenceErrors candidate evidence with
            | _ :: _ as errors -> Error errors
            | [] ->
                match TargetSourceAnchor.tryReadMethod assemblyPath evidence.MethodToken with
                | Error error -> Error [ SourceError error ]
                | Ok record ->
                    match identityErrors candidate evidence record with
                    | _ :: _ as errors -> Error errors
                    | [] ->
                        let size = evidence.Regions.Head.Size
                        let ilSize = record.IlBodySize.Value

                        match decode ilSize size entries with
                        | Error errors -> Error errors
                        | Ok(sized, uncovered) ->
                            Ok { ResolverLabel = evidence.Label; ResolverSymbol = symbol; ResolverStart = evidence.CodeStart; ResolverSize = size
                                 ResolverEntries = sized
                                 ResolverUncovered = uncovered |> Array.map (fun (s, f) -> evidence.CodeStart + uint64 s, evidence.CodeStart + uint64 f)
                                 ResolverPoints = record.Points |> Array.sortBy (fun p -> p.Offset)
                                 ResolverIlBodySize = ilSize
                                 ResolverLimitations = limitations evidence.Label }

    let internal lineFor (points: TargetSourceAnchor.Point array) (ilBodySize: int) (il: uint32) =
        match points |> Array.tryFindIndexBack (fun p -> uint32 p.Offset <= il) with
        | None -> NoSequencePoint il
        | Some index when points[index].Hidden -> HiddenSequencePoint il
        | Some index ->
            let point = points[index]
            let ilEnd = if index + 1 < points.Length then points[index + 1].Offset else ilBodySize
            Line { Document = point.Document; StartLine = point.Line; StartColumn = point.Column; EndLine = point.EndLine; EndColumn = point.EndColumn
                   IlStart = point.Offset; IlEnd = ilEnd }

    let resolve (resolver: TargetLineResolver) (ip: uint64) =
        if ip < resolver.ResolverStart || ip - resolver.ResolverStart >= resolver.ResolverSize then
            OutsideBody
        else
            let offset = uint32 (ip - resolver.ResolverStart)

            match resolver.ResolverEntries |> Array.tryFind (fun (struct (s, f, _)) -> s <= offset && offset < f) with
            | None -> Uncovered
            | Some(struct (_, _, MapProlog)) -> InProlog
            | Some(struct (_, _, MapEpilog)) -> InEpilog
            | Some(struct (_, _, MapNone)) -> Unmapped
            | Some(struct (_, _, MapIl il)) -> lineFor resolver.ResolverPoints resolver.ResolverIlBodySize il
