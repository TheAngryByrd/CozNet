namespace CozNet

open System
open System.Buffers.Binary

type TargetMapCaptureOutcome =
    | Captured
    | InfoUnavailable of hr: int
    | SelectionInvalid of hr: int
    | EventMaskFailed of hr: int
    | JitFailed of hr: int
    | MetadataFailed of hr: int
    | MvidMismatch
    | StartsFailed of hr: int
    | NoNativeBody
    | MultipleBodies
    | CodeInfoFailed of hr: int
    | NoCodeRegion
    | CodeInfoUnexpectedCount
    | MapFailed of hr: int
    | MapEmpty
    | MapTooLarge
    | MapCountChanged of firstCount: uint32
    | InternalError of hr: int

type TargetMapCaptureStatus =
    | NotSeen
    | Completed of TargetMapCaptureOutcome

type TargetMapCaptureFlag =
    | SecondNotification
    | UnloadDuringCapture
    | UnloadedAfterCapture
    | Closed

type TargetMapCaptureCounters =
    { FunctionInfoFailures: uint32
      ModuleInfoFailures: uint32
      NameTruncations: uint32
      UnsafeCallbacks: uint32 }

type TargetMapCaptureParseError =
    | TooShort of length: int
    | BadMagic of magic: uint32
    | UnsupportedVersion of version: uint16
    | UnexpectedHeaderSize of headerSize: uint16
    | LengthMismatch of declared: uint32 * actual: int
    | UnknownState of state: uint32
    | UnknownOutcome of outcome: uint32
    | UnknownFlags of flags: uint32
    | UnexpectedCapacity of capacity: uint32
    | NonZeroReserved of offset: int
    | CountExceeded of field: string * reported: uint32 * copied: uint32 * limit: uint32
    | InconsistentRecord of reason: string

type TargetMapCaptureRecord = private {
    RecordStatus: TargetMapCaptureStatus
    RecordFlags: Set<TargetMapCaptureFlag>
    RecordFunctionId: uint64
    RecordModuleId: uint64
    RecordModuleVersionId: Guid
    RecordSelectedModuleVersionId: Guid
    RecordMethodToken: uint32
    RecordBodiesReported: uint32
    RecordBodyStarts: uint64 array
    RecordRegionsReported: uint32
    RecordRegions: TargetCodeRegion list
    RecordEntriesReported: uint32
    RecordEntries: struct (uint32 * uint32 * uint32) array
    RecordCounters: TargetMapCaptureCounters
    RecordModuleNameReported: uint32
    RecordModuleName: string
} with
    member this.Status = this.RecordStatus
    member this.Flags = this.RecordFlags
    member this.FunctionId = this.RecordFunctionId
    member this.ModuleId = this.RecordModuleId
    member this.ModuleVersionId = this.RecordModuleVersionId
    member this.SelectedModuleVersionId = this.RecordSelectedModuleVersionId
    member this.MethodToken = this.RecordMethodToken
    member this.BodiesReported = this.RecordBodiesReported
    member this.BodyStarts = Array.copy this.RecordBodyStarts
    member this.RegionsReported = this.RecordRegionsReported
    member this.Regions = this.RecordRegions
    member this.EntriesReported = this.RecordEntriesReported
    member this.Entries = Array.copy this.RecordEntries
    member this.Counters = this.RecordCounters
    member this.ModuleNameReported = this.RecordModuleNameReported
    member this.ModuleName = this.RecordModuleName

type TargetMapCaptureSource =
    private
    | SyntheticCapture of label: string
    | LiveCapture of processId: int * librarySha256: string

type TargetMapEvidenceError =
    | CaptureNotSeen
    | CaptureFailed of TargetMapCaptureOutcome
    | CaptureInvalidated of TargetMapCaptureFlag list

[<RequireQualifiedAccess>]
module TargetMapCaptureSource =
    let trySynthetic (label: string) =
        if String.IsNullOrWhiteSpace label then Error "The synthetic capture label must not be blank."
        else Ok(SyntheticCapture label)

    let internal tryLive (processId: int) (librarySha256: string) =
        if processId <= 0 then Error $"The process id {processId} must be positive."
        elif isNull librarySha256 || librarySha256.Length <> 64 || not (librarySha256 |> Seq.forall (fun c -> Char.IsAsciiDigit c || (c >= 'a' && c <= 'f'))) then
            Error "The profiler library SHA-256 must be 64 lowercase hexadecimal characters."
        else Ok(LiveCapture(processId, librarySha256))

    let internal label source =
        match source with
        | SyntheticCapture label -> $"synthetic: {label}"
        | LiveCapture(processId, sha) -> $"captured: pid {processId} profiler {sha}"

[<RequireQualifiedAccess>]
module TargetMapCapture =
    let magic = 0x504D5A43u
    let version = 1us
    let headerSize = 704
    let entrySize = 12
    let entryCapacity = 4096u
    let nameCapacity = 260u

    let private flagBits =
        [ 1u, SecondNotification; 2u, UnloadDuringCapture; 4u, UnloadedAfterCapture; 8u, Closed ]

    let private failed hr = hr < 0

    let private outcomeOf (code: uint32) (hr: int) (detail: uint32) =
        let withHr make =
            if failed hr then Ok(make hr) else Error(InconsistentRecord $"outcome {code} needs a failed HRESULT, not 0x{hr:X8}")
        let plain value =
            if hr = 0 then Ok value else Error(InconsistentRecord $"outcome {code} needs HRESULT 0, not 0x{hr:X8}")
        let outcome =
            match code with
            | 1u -> plain Captured
            | 2u -> withHr InfoUnavailable
            | 3u -> withHr SelectionInvalid
            | 4u -> withHr EventMaskFailed
            | 5u -> withHr JitFailed
            | 6u -> withHr MetadataFailed
            | 7u -> plain MvidMismatch
            | 8u -> withHr StartsFailed
            | 9u -> plain NoNativeBody
            | 10u -> plain MultipleBodies
            | 11u -> withHr CodeInfoFailed
            | 12u -> plain NoCodeRegion
            | 13u -> plain CodeInfoUnexpectedCount
            | 14u -> withHr MapFailed
            | 15u -> plain MapEmpty
            | 16u -> plain MapTooLarge
            | 17u -> plain(MapCountChanged detail)
            | 18u -> withHr InternalError
            | other -> Error(UnknownOutcome other)
        match outcome with
        | Ok(MapCountChanged _) when detail = 0u -> Error(InconsistentRecord "MapCountChanged needs the first map count")
        | Ok(MapCountChanged _) -> outcome
        | Ok _ when detail <> 0u -> Error(InconsistentRecord $"outcome {code} has detail {detail}")
        | _ -> outcome

    let private unclaimed status name =
        match status with
        | NotSeen
        | Completed(InfoUnavailable _ | SelectionInvalid _ | EventMaskFailed _) -> true
        | Completed(InternalError _) -> name = (0u, 0u)
        | Completed _ -> false

    let private shapeError status (flags: uint32) bodies regions entries name mvid selected =
        let none = (0u, 0u)
        let one = (1u, 1u)
        let whole (reported, copied) = reported = copied && copied > 0u
        let expect ok reason = if ok then None else Some(InconsistentRecord reason)
        match status with
        | _ when unclaimed status name ->
            expect (bodies = none && regions = none && entries = none && name = none && mvid = Guid.Empty) "a record without a claim has capture fields"
            |> Option.orElse (expect (flags &&& ~~~8u = 0u) "a record without a claim can only be Closed")
        | NotSeen -> None
        | Completed(InternalError _) -> expect (whole name) "a claimed record needs the module name"
        | Completed outcome ->
            let counts =
                match outcome with
                | JitFailed _ | MetadataFailed _ | MvidMismatch | NoNativeBody -> bodies = none && regions = none && entries = none
                | StartsFailed _ -> snd bodies = 0u && regions = none && entries = none
                | MultipleBodies -> bodies = (2u, 2u) && regions = none && entries = none
                | CodeInfoFailed _ -> bodies = one && snd regions = 0u && entries = none
                | NoCodeRegion -> bodies = one && regions = none && entries = none
                | CodeInfoUnexpectedCount -> bodies = one && fst regions > 2u && snd regions = 2u && entries = none
                | MapFailed _ -> bodies = one && whole regions && snd entries = 0u
                | MapEmpty -> bodies = one && whole regions && entries = none
                | MapTooLarge -> bodies = one && whole regions && fst entries > entryCapacity && snd entries = 0u
                | MapCountChanged first -> bodies = one && whole regions && first <> fst entries && snd entries = min first (fst entries)
                | Captured -> bodies = one && whole regions && whole entries
                | InfoUnavailable _ | SelectionInvalid _ | EventMaskFailed _ | InternalError _ -> true
            let identity =
                match outcome with
                | JitFailed _ -> mvid = Guid.Empty
                | MetadataFailed _ -> true
                | MvidMismatch -> mvid <> selected
                | _ -> mvid = selected
            expect (whole name) "a claimed record needs the module name"
            |> Option.orElse (expect counts $"the counts do not fit {outcome}")
            |> Option.orElse (expect identity $"the MVID does not fit {outcome}")

    let tryParse (bytes: byte array) : Result<TargetMapCaptureRecord, TargetMapCaptureParseError> =
        if isNull bytes || bytes.Length < headerSize then Error(TooShort(if isNull bytes then 0 else bytes.Length))
        else
            let u16 offset = BinaryPrimitives.ReadUInt16LittleEndian(ReadOnlySpan(bytes, offset, 2))
            let u32 offset = BinaryPrimitives.ReadUInt32LittleEndian(ReadOnlySpan(bytes, offset, 4))
            let u64 offset = BinaryPrimitives.ReadUInt64LittleEndian(ReadOnlySpan(bytes, offset, 8))
            let guid offset = Guid(ReadOnlySpan(bytes, offset, 16))
            let zero first last =
                seq { first .. last - 1 } |> Seq.tryFind (fun offset -> bytes[offset] <> 0uy) |> Option.map NonZeroReserved
            let counted field reportedAt limit =
                let reported, copied = u32 reportedAt, u32 (reportedAt + 4)
                if copied > reported || copied > limit then Error(CountExceeded(field, reported, copied, limit)) else Ok(reported, copied)
            let checks =
                [ fun () -> if u32 0 <> magic then Some(BadMagic(u32 0)) else None
                  fun () -> if u16 4 <> version then Some(UnsupportedVersion(u16 4)) else None
                  fun () -> if int (u16 6) <> headerSize then Some(UnexpectedHeaderSize(u16 6)) else None
                  fun () -> if u32 108 <> entryCapacity then Some(UnexpectedCapacity(u32 108)) else None
                  fun () ->
                      let declared = u32 8
                      if uint64 declared <> uint64 bytes.Length then Some(LengthMismatch(declared, bytes.Length))
                      elif uint64 declared <> uint64 headerSize + uint64 entrySize * uint64 (u32 104) then
                          Some(InconsistentRecord $"total size {declared} does not hold {u32 104} entries")
                      else None
                  fun () ->
                      let unknown = flagBits |> List.fold (fun bits (bit, _) -> bits &&& ~~~bit) (u32 24)
                      if unknown <> 0u then Some(UnknownFlags(u32 24)) else None ]
            match checks |> List.tryPick (fun check -> check ()) with
            | Some error -> Error error
            | None ->
                let status =
                    match u32 12, u32 16 with
                    | 0u, 0u when u32 20 = 0u && u32 28 = 0u -> Ok NotSeen
                    | 0u, _ -> Error(InconsistentRecord "a NotSeen record has an outcome")
                    | 2u, code -> outcomeOf code (int (u32 20)) (u32 28) |> Result.map Completed
                    | state, _ -> Error(UnknownState state)
                let counts =
                    counted "bodies" 84 2u
                    |> Result.bind (fun bodies -> counted "regions" 92 2u |> Result.map (fun regions -> bodies, regions))
                    |> Result.bind (fun (bodies, regions) -> counted "entries" 100 entryCapacity |> Result.map (fun entries -> bodies, regions, entries))
                    |> Result.bind (fun (bodies, regions, entries) -> counted "module name" 176 nameCapacity |> Result.map (fun name -> bodies, regions, entries, name))
                match status, counts with
                | Error error, _ | _, Error error -> Error error
                | Ok status, Ok(bodies, regions, entries, name) ->
                    let bodiesCopied, regionsCopied, nameCopied = int (snd bodies), int (snd regions), int (snd name)
                    let mvid, selected = guid 48, guid 64
                    let reserved =
                        [ zero (112 + 8 * bodiesCopied) 128
                          zero (128 + 16 * regionsCopied) 160
                          zero (184 + 2 * nameCopied) headerSize
                          (if unclaimed status name then [ zero 32 48; zero 80 84 ] |> List.tryPick id else None) ]
                        |> List.tryPick id
                    match reserved, shapeError status (u32 24) bodies regions entries name mvid selected with
                    | Some error, _ | None, Some error -> Error error
                    | None, None ->
                        Ok { RecordStatus = status
                             RecordFlags = flagBits |> List.filter (fun (bit, _) -> u32 24 &&& bit <> 0u) |> List.map snd |> Set.ofList
                             RecordFunctionId = u64 32
                             RecordModuleId = u64 40
                             RecordModuleVersionId = mvid
                             RecordSelectedModuleVersionId = selected
                             RecordMethodToken = u32 80
                             RecordBodiesReported = fst bodies
                             RecordBodyStarts = Array.init bodiesCopied (fun i -> u64 (112 + 8 * i))
                             RecordRegionsReported = fst regions
                             RecordRegions = List.init regionsCopied (fun i -> { Start = u64 (128 + 16 * i); Size = u64 (136 + 16 * i) })
                             RecordEntriesReported = fst entries
                             RecordEntries =
                                 Array.init (int (snd entries)) (fun i ->
                                     let offset = headerSize + entrySize * i
                                     struct (u32 offset, u32 (offset + 4), u32 (offset + 8)))
                             RecordCounters =
                                 { FunctionInfoFailures = u32 160; ModuleInfoFailures = u32 164; NameTruncations = u32 168; UnsafeCallbacks = u32 172 }
                             RecordModuleNameReported = fst name
                             RecordModuleName = String(Array.init nameCopied (fun i -> char (u16 (184 + 2 * i)))) }

    let toEvidence (source: TargetMapCaptureSource) (record: TargetMapCaptureRecord) : Result<TargetCodeEvidence, TargetMapEvidenceError> =
        match record.RecordStatus with
        | NotSeen -> Error CaptureNotSeen
        | Completed outcome when outcome <> Captured -> Error(CaptureFailed outcome)
        | Completed _ when not record.RecordFlags.IsEmpty -> Error(CaptureInvalidated(Set.toList record.RecordFlags))
        | Completed _ ->
            Ok { Label = TargetMapCaptureSource.label source
                 ModuleVersionId = record.RecordModuleVersionId
                 MethodToken = int record.RecordMethodToken
                 CodeStart = record.RecordBodyStarts[0]
                 Regions = record.RecordRegions
                 NativeBodyCount = int record.RecordBodiesReported
                 Entries = Array.copy record.RecordEntries }
