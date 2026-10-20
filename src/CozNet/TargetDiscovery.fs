namespace CozNet

open System
open System.IO
open System.Reflection
open System.Reflection.Metadata
open System.Reflection.Metadata.Ecma335
open System.Reflection.PortableExecutable

type PerfMapMethod = {
    Assembly: string
    Symbol: string
    Tier: string
}

type PerfMapEntry = {
    Start: uint64
    End: uint64
    Label: string
    Method: PerfMapMethod option
}

type PerfMapIndexError =
    | MalformedLine of lineNumber: int * line: string
    | OverlappingRanges of first: PerfMapEntry * second: PerfMapEntry

type PerfMapIndex = private {
    IndexEntries: PerfMapEntry array
    IndexLines: string array
} with
    member this.Entries = Array.copy this.IndexEntries

    member this.TryFind(ip: uint64) =
        let entries = this.IndexEntries
        let mutable low = 0
        let mutable high = entries.Length - 1
        let mutable found = None

        while found.IsNone && low <= high do
            let middle = low + (high - low) / 2
            let entry = entries[middle]

            if ip < entry.Start then high <- middle - 1
            elif ip >= entry.End then low <- middle + 1
            else found <- Some entry

        found

[<RequireQualifiedAccess>]
module PerfMapIndex =
    let private hex (text: string) =
        let digits = if text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) then text.Substring 2 else text
        UInt64.TryParse(digits, Globalization.NumberStyles.AllowHexSpecifier, Globalization.CultureInfo.InvariantCulture)

    let internal tryMethod (label: string) =
        let tierStart = label.LastIndexOf '['

        if tierStart <= 0 || not (label.EndsWith ']') then
            None
        else
            let tier = label.Substring tierStart
            let head = label.Substring(0, tierStart)
            let rec find (from: int) =
                let open' = head.IndexOf(" [", from, StringComparison.Ordinal)

                if open' < 0 then
                    None
                else
                    let close = head.IndexOf("] ", open' + 2, StringComparison.Ordinal)
                    let assembly = if close > open' + 2 then head.Substring(open' + 2, close - open' - 2) else ""

                    if close > 0 && assembly.Length > 0 && not (assembly.Contains ' ' || assembly.Contains '[' || assembly.Contains ']') then
                        let symbol = head.Substring(close + 2)

                        if symbol.Contains("::", StringComparison.Ordinal) && symbol.EndsWith ')' then
                            Some { Assembly = assembly; Symbol = symbol; Tier = tier }
                        else
                            None
                    else
                        find (open' + 1)

            find 0

    let tryCreate (lines: string seq) =
        let lines = Seq.toArray lines
        let parsed =
            lines
            |> Array.mapi (fun index (line: string) ->
                if String.IsNullOrWhiteSpace line then
                    Ok None
                else
                    let parts = line.Split(' ', 3)

                    if parts.Length <> 3 || String.IsNullOrWhiteSpace parts[2] then
                        Error(MalformedLine(index + 1, line))
                    else
                        match hex parts[0], hex parts[1] with
                        | (true, start), (true, size) when size > 0UL && start <= UInt64.MaxValue - size ->
                            Ok(Some { Start = start; End = start + size; Label = parts[2]; Method = tryMethod parts[2] })
                        | _ -> Error(MalformedLine(index + 1, line)))

        match parsed |> Array.tryPick (function Error error -> Some error | Ok _ -> None) with
        | Some error -> Error error
        | None ->
            let entries = parsed |> Array.choose (function Ok entry -> entry | Error _ -> None) |> Array.sortBy (fun entry -> entry.Start, entry.End)

            match entries |> Array.pairwise |> Array.tryFind (fun (first, second) -> second.Start < first.End) with
            | Some(first, second) -> Error(OverlappingRanges(first, second))
            | None -> Ok { IndexEntries = entries; IndexLines = lines }

    let internal lines (index: PerfMapIndex) = index.IndexLines

    let describe error =
        match error with
        | MalformedLine(number, line) -> $"Perf-map line {number} is not '<start> <size> <label>' with a positive size: {line}"
        | OverlappingRanges(first, second) -> $"Perf-map ranges overlap: '{first.Label}' and '{second.Label}'."

type TargetDiscoveryScopeError =
    | NoInclude
    | BlankPrefix

type TargetDiscoveryScope = private {
    ScopeInclude: string array
    ScopeExclude: string array
} with
    member this.Include = Array.copy this.ScopeInclude
    member this.Exclude = Array.copy this.ScopeExclude

[<RequireQualifiedAccess>]
module TargetDiscoveryScope =
    let internal profilerAssembly = typeof<TargetDiscoveryScope>.Assembly.GetName().Name

    let tryCreate (includes: string list) (excludes: string list) =
        if List.isEmpty includes then Error NoInclude
        elif includes @ excludes |> List.exists String.IsNullOrWhiteSpace then Error BlankPrefix
        else Ok { ScopeInclude = List.toArray includes; ScopeExclude = List.toArray excludes }

    let contains (scope: TargetDiscoveryScope) (method: PerfMapMethod) =
        let starts (prefix: string) = method.Symbol.StartsWith(prefix, StringComparison.Ordinal)
        not (String.Equals(method.Assembly, profilerAssembly, StringComparison.Ordinal))
        && Array.exists starts scope.ScopeInclude
        && not (Array.exists starts scope.ScopeExclude)

type TargetDiscoveryLimit = private { LimitCount: int } with
    member this.Count = this.LimitCount

[<RequireQualifiedAccess>]
module TargetDiscoveryLimit =
    let tryCreate (count: int) =
        if count < 1 then Error $"A discovery limit must be at least 1, not {count}." else Ok { LimitCount = count }

type TargetNotBindableCounts = {
    NotMethodLabel: int64
    NotOptimized: int64
    NotUnique: int64
}

type TargetDiscoveryCounts = {
    Samples: int64
    Unmapped: int64
    NotBindable: TargetNotBindableCounts
    OutOfScope: int64
    InScope: int64
}

type DiscoveredMethod = {
    Symbol: string
    Assembly: string
    Start: uint64
    End: uint64
    Samples: int64
    WorkerSamples: (int * int64) array
}

type TargetDiscoveryResult = {
    Counts: TargetDiscoveryCounts
    Ranked: DiscoveredMethod array
    OmittedMethods: int
    OmittedSamples: int64
}

type TargetDiscoveryError =
    | NoSamples
    | NoSamplesInScope of counts: TargetDiscoveryCounts
    | RunIncomplete of reasons: string list
    | DelayAdded of globalNs: uint64 * creditedSamples: int

[<RequireQualifiedAccess>]
module TargetDiscovery =
    let private optimizedTier = "[Optimized]"

    type private Class =
        | NotMethodLabel
        | NotOptimized
        | NotUnique
        | OutOfScope
        | InScope of PerfMapEntry * PerfMapMethod

    let rankEventsUnchecked (scope: TargetDiscoveryScope) (index: PerfMapIndex) (events: TargetLedgerEvent array) (limit: TargetDiscoveryLimit) =
        let samples = events |> Array.filter (fun e -> e.Kind = TargetLedgerEventKind.Sample)

        if samples.Length = 0 then
            Error NoSamples
        else
            let lines = PerfMapIndex.lines index
            let classes = Collections.Generic.Dictionary<uint64, Class>()

            let classify (entry: PerfMapEntry) =
                match classes.TryGetValue entry.Start with
                | true, known -> known
                | _ ->
                    let value =
                        match entry.Method with
                        | None -> NotMethodLabel
                        | Some method when not (String.Equals(method.Tier, optimizedTier, StringComparison.Ordinal)) -> NotOptimized
                        | Some method ->
                            match CodeBinding.tryCapture lines [| method.Symbol |] with
                            | Ok binding when binding.Range method.Symbol = (entry.Start, entry.End) ->
                                if TargetDiscoveryScope.contains scope method then InScope(entry, method) else OutOfScope
                            | _ -> NotUnique
                    classes[entry.Start] <- value
                    value

            let mutable unmapped, notMethod, notOptimized, notUnique, outOfScope = 0L, 0L, 0L, 0L, 0L
            let found = Collections.Generic.Dictionary<uint64, PerfMapEntry * PerfMapMethod * Collections.Generic.Dictionary<int, int64>>()

            for sample in samples do
                match index.TryFind sample.Ip with
                | None -> unmapped <- unmapped + 1L
                | Some entry ->
                    match classify entry with
                    | NotMethodLabel -> notMethod <- notMethod + 1L
                    | NotOptimized -> notOptimized <- notOptimized + 1L
                    | NotUnique -> notUnique <- notUnique + 1L
                    | OutOfScope -> outOfScope <- outOfScope + 1L
                    | InScope(entry, method) ->
                        let _, _, workers =
                            match found.TryGetValue entry.Start with
                            | true, value -> value
                            | _ ->
                                let value = entry, method, Collections.Generic.Dictionary<int, int64>()
                                found[entry.Start] <- value
                                value
                        workers[sample.Worker] <- (match workers.TryGetValue sample.Worker with | true, count -> count | _ -> 0L) + 1L

            let methods =
                found.Values
                |> Seq.map (fun (entry, method, workers) ->
                    let perWorker = workers |> Seq.map (fun pair -> pair.Key, pair.Value) |> Seq.sortBy fst |> Seq.toArray
                    { Symbol = method.Symbol; Assembly = method.Assembly; Start = entry.Start; End = entry.End
                      Samples = perWorker |> Array.sumBy snd; WorkerSamples = perWorker })
                |> Seq.toArray
                |> Array.sortWith (fun a b ->
                    match compare b.Samples a.Samples with
                    | 0 -> String.CompareOrdinal(a.Symbol, b.Symbol)
                    | order -> order)

            let inScope = methods |> Array.sumBy (fun m -> m.Samples)
            let counts = { TargetDiscoveryCounts.Samples = int64 samples.Length; Unmapped = unmapped
                           NotBindable = { NotMethodLabel = notMethod; NotOptimized = notOptimized; NotUnique = notUnique }
                           OutOfScope = outOfScope; InScope = inScope }

            if inScope = 0L then
                Error(NoSamplesInScope counts)
            else
                let kept = min limit.Count methods.Length
                let omitted = methods |> Array.skip kept
                Ok { Counts = counts; Ranked = Array.take kept methods; OmittedMethods = omitted.Length; OmittedSamples = omitted |> Array.sumBy (fun m -> m.Samples) }

    let internal incompleteReasons (run: TargetProfileResult) =
        [ if not run.Passed then "the run did not pass"
          match run.BindingError with
          | Some message -> $"the binding failed: {message}"
          | None -> ()
          if run.CloseCode <> 0 then $"the close code is {run.CloseCode}"
          if run.Experiment.Failure <> 0u then $"the experiment failure code is {run.Experiment.Failure}"
          for worker, thread in Array.indexed run.Threads do
              if thread.Result.Failure <> 0 then $"worker {worker} has failure code {thread.Result.Failure}"
              if thread.Result.Lost <> 0UL then $"worker {worker} lost {thread.Result.Lost} samples"
              if thread.FinishCode <> 0 then $"worker {worker} has finish code {thread.FinishCode}"
          match run.Replay with
          | Error message -> $"the replay failed: {message}"
          | Ok _ -> () ]

    let rank (scope: TargetDiscoveryScope) (index: PerfMapIndex) (run: TargetProfileResult) (limit: TargetDiscoveryLimit) =
        match incompleteReasons run with
        | [] ->
            let credited = run.Events |> Array.filter (fun e -> e.Kind = TargetLedgerEventKind.Sample && e.Amount > 0UL) |> Array.length

            if run.GlobalNs <> 0UL || credited > 0 then Error(DelayAdded(run.GlobalNs, credited))
            else rankEventsUnchecked scope index run.Events limit
        | reasons -> Error(RunIncomplete reasons)

type TargetSymbolError =
    | NoDeclaringType
    | GenericMethod
    | GenericType of typeName: string
    | UnsupportedParameter of position: int * typeName: string

[<RequireQualifiedAccess>]
module TargetSymbol =
    let private keywords =
        dict [
            typeof<bool>, "bool"; typeof<char>, "char"; typeof<sbyte>, "int8"; typeof<byte>, "uint8"; typeof<int16>, "int16"
            typeof<uint16>, "uint16"; typeof<int32>, "int32"; typeof<uint32>, "uint32"; typeof<int64>, "int64"; typeof<uint64>, "uint64"
            typeof<float32>, "float32"; typeof<float>, "float64"; typeof<nativeint>, "native int"; typeof<unativeint>, "native uint"
            typeof<string>, "string"; typeof<obj>, "object" ]

    let private parameter (position: int) (t: Type) =
        let element (inner: Type) suffix =
            match keywords.TryGetValue inner with
            | true, name -> Ok(name + suffix)
            | _ -> Error(UnsupportedParameter(position, t.ToString()))

        match keywords.TryGetValue t with
        | true, name -> Ok name
        | _ when t.IsSZArray -> element (t.GetElementType()) "[]"
        | _ when t.IsByRef -> element (t.GetElementType()) "&"
        | _ when t.IsPointer -> element (t.GetElementType()) "*"
        | _ -> Error(UnsupportedParameter(position, t.ToString()))

    let tryFormat (method: MethodInfo) =
        let declaring = method.DeclaringType

        if isNull declaring then
            Error NoDeclaringType
        elif method.IsGenericMethod then
            Error GenericMethod
        else
            let rec generic (t: Type) = not (isNull t) && (t.IsGenericType || generic t.DeclaringType)

            if generic declaring then
                Error(GenericType(declaring.ToString()))
            else
                let parameters = method.GetParameters() |> Array.mapi (fun i p -> parameter i p.ParameterType)

                match parameters |> Array.tryPick (function Error error -> Some error | Ok _ -> None) with
                | Some error -> Error error
                | None ->
                    let names = parameters |> Array.choose (function Ok name -> Some name | Error _ -> None)
                    Ok $"""{declaring.FullName}::{method.Name}({String.Join(",", names)})"""

type TargetSourceAnchor = {
    Document: string
    Line: int
    Column: int
}

type TargetSourceAnchorError =
    | NoAssemblyFile
    | NoDebugDirectory
    | NotPortablePdb
    | PdbNotFound of searched: string list
    | PdbMismatch of path: string
    | PdbMalformed of path: string * message: string
    | PdbUnreadable of path: string * message: string
    | AssemblyMalformed of path: string * message: string
    | AssemblyUnreadable of path: string * message: string
    | MethodNotInPdb of metadataToken: int
    | OnlyHiddenSequencePoints of metadataToken: int

[<RequireQualifiedAccess>]
module TargetSourceAnchor =
    type internal Point = {
        Offset: int
        Hidden: bool
        Document: string
        Line: int
        Column: int
        EndLine: int
        EndColumn: int
    }

    type internal MethodRecord = {
        ModuleVersionId: Guid
        AssemblyName: string
        TypeName: string
        MethodName: string
        Parameters: string
        Generic: bool
        IlBodySize: int option
        Points: Point array
    }

    let internal unsupportedType = "?"

    let private baseNames =
        set [ "bool"; "char"; "int8"; "uint8"; "int16"; "uint16"; "int32"; "uint32"; "int64"; "uint64"; "float32"; "float64"
              "native int"; "native uint"; "string"; "object" ]

    let private wrap (element: string) (suffix: string) = if baseNames.Contains element then element + suffix else unsupportedType

    let internal primitiveName (code: PrimitiveTypeCode) =
        match code with
        | PrimitiveTypeCode.Boolean -> "bool"
        | PrimitiveTypeCode.Char -> "char"
        | PrimitiveTypeCode.SByte -> "int8"
        | PrimitiveTypeCode.Byte -> "uint8"
        | PrimitiveTypeCode.Int16 -> "int16"
        | PrimitiveTypeCode.UInt16 -> "uint16"
        | PrimitiveTypeCode.Int32 -> "int32"
        | PrimitiveTypeCode.UInt32 -> "uint32"
        | PrimitiveTypeCode.Int64 -> "int64"
        | PrimitiveTypeCode.UInt64 -> "uint64"
        | PrimitiveTypeCode.Single -> "float32"
        | PrimitiveTypeCode.Double -> "float64"
        | PrimitiveTypeCode.IntPtr -> "native int"
        | PrimitiveTypeCode.UIntPtr -> "native uint"
        | PrimitiveTypeCode.String -> "string"
        | PrimitiveTypeCode.Object -> "object"
        | _ -> unsupportedType

    type private SignatureNames() =
        interface ISignatureTypeProvider<string, obj> with
            member _.GetPrimitiveType code = primitiveName code
            member _.GetSZArrayType element = wrap element "[]"
            member _.GetByReferenceType element = wrap element "&"
            member _.GetPointerType element = wrap element "*"
            member _.GetArrayType(_, _) = unsupportedType
            member _.GetFunctionPointerType _ = unsupportedType
            member _.GetGenericInstantiation(_, _) = unsupportedType
            member _.GetGenericMethodParameter(_, _) = unsupportedType
            member _.GetGenericTypeParameter(_, _) = unsupportedType
            member _.GetModifiedType(_, _, _) = unsupportedType
            member _.GetPinnedType _ = unsupportedType
            member _.GetTypeFromDefinition(_, _, _) = unsupportedType
            member _.GetTypeFromReference(_, _, _) = unsupportedType
            member _.GetTypeFromSpecification(_, _, _, _) = unsupportedType

    type private PeFacts = {
        Facts: MethodRecord
    }

    let internal firstVisible (points: Point seq) : TargetSourceAnchor option =
        points
        |> Seq.tryFind (fun point -> not point.Hidden)
        |> Option.map (fun point -> { TargetSourceAnchor.Document = point.Document; Line = point.Line; Column = point.Column })

    let private methodHandle (rows: int) (metadataToken: int) =
        try
            let handle = MetadataTokens.MethodDefinitionHandle(metadataToken &&& 0x00FFFFFF)
            let row = MetadataTokens.GetRowNumber handle
            if (metadataToken >>> 24) = 0x06 && row >= 1 && row <= rows then Some handle else None
        with :? ArgumentException -> None

    let private pointsOf (reader: MetadataReader) (metadataToken: int) =
        match methodHandle reader.MethodDebugInformation.Count metadataToken with
        | None -> Error(MethodNotInPdb metadataToken)
        | Some handle ->
            let information = reader.GetMethodDebugInformation(handle.ToDebugInformationHandle())

            if information.SequencePointsBlob.IsNil then
                Error(MethodNotInPdb metadataToken)
            else
                information.GetSequencePoints()
                |> Seq.map (fun point ->
                    { Offset = point.Offset
                      Hidden = point.IsHidden
                      Document = (if point.IsHidden || point.Document.IsNil then "" else reader.GetString(reader.GetDocument(point.Document).Name))
                      Line = point.StartLine
                      Column = point.StartColumn
                      EndLine = point.EndLine
                      EndColumn = point.EndColumn })
                |> Seq.toArray
                |> Ok

    let private anchor () (reader: MetadataReader) (metadataToken: int) =
        match pointsOf reader metadataToken with
        | Error error -> Error error
        | Ok points ->
            match firstVisible points with
            | Some found -> Ok found
            | None when points.Length = 0 -> Error(MethodNotInPdb metadataToken)
            | None -> Error(OnlyHiddenSequencePoints metadataToken)

    let private noFacts (_: string) (_: PEReader) (_: int) : Result<unit, TargetSourceAnchorError> = Ok()

    let private peFacts (assemblyPath: string) (pe: PEReader) (metadataToken: int) =
        if not pe.HasMetadata then
            Error(AssemblyMalformed(assemblyPath, "The file has no metadata."))
        else
        let metadata = pe.GetMetadataReader()

        if not metadata.IsAssembly then
            Error(AssemblyMalformed(assemblyPath, "The module has no assembly manifest."))
        else
            match methodHandle metadata.MethodDefinitions.Count metadataToken with
            | None -> Error(MethodNotInPdb metadataToken)
            | Some handle ->
                let definition = metadata.GetMethodDefinition handle
                let rec typeName (typeHandle: TypeDefinitionHandle) =
                    let t = metadata.GetTypeDefinition typeHandle
                    let name = metadata.GetString t.Name
                    if t.GetDeclaringType().IsNil then
                        let ns = metadata.GetString t.Namespace
                        (if ns = "" then name else ns + "." + name), t.GetGenericParameters().Count > 0
                    else
                        let outer, generic = typeName (t.GetDeclaringType())
                        outer + "+" + name, generic || t.GetGenericParameters().Count > 0
                let declaring, genericType = typeName (definition.GetDeclaringType())
                let signature = definition.DecodeSignature(SignatureNames(), null)
                let size = if definition.RelativeVirtualAddress = 0 then None else Some((pe.GetMethodBody definition.RelativeVirtualAddress).GetILContent().Length)
                Ok { Facts =
                        { ModuleVersionId = metadata.GetGuid(metadata.GetModuleDefinition().Mvid)
                          AssemblyName = metadata.GetString(metadata.GetAssemblyDefinition().Name)
                          TypeName = declaring
                          MethodName = metadata.GetString definition.Name
                          Parameters = String.Join(",", signature.ParameterTypes)
                          Generic = genericType || definition.GetGenericParameters().Count > 0
                          IlBodySize = size
                          Points = [||] } }

    let private methodRecord (facts: PeFacts) (pdb: MetadataReader) (metadataToken: int) =
        match pointsOf pdb metadataToken with
        | Error error -> Error error
        | Ok points -> Ok { facts.Facts with Points = points }

    let private read (assemblyPath: string) (metadataToken: int) (prepare: string -> PEReader -> int -> Result<'p, TargetSourceAnchorError>)
        (select: 'p -> MetadataReader -> int -> Result<'a, TargetSourceAnchorError>) =
            use stream = File.OpenRead assemblyPath
            use pe = new PEReader(stream)
            match prepare assemblyPath pe metadataToken with
            | Error error -> Error error
            | Ok prepared ->
            let entries = pe.ReadDebugDirectory()

            match entries |> Seq.tryFind (fun e -> e.Type = DebugDirectoryEntryType.EmbeddedPortablePdb) with
            | Some embedded ->
                try
                    use provider = pe.ReadEmbeddedPortablePdbDebugDirectoryData embedded
                    select prepared (provider.GetMetadataReader()) metadataToken
                with :? BadImageFormatException as e -> Error(PdbMalformed(assemblyPath, e.Message))
            | None ->
                match entries |> Seq.tryFind (fun e -> e.Type = DebugDirectoryEntryType.CodeView) with
                | None -> Error NoDebugDirectory
                | Some codeView when not codeView.IsPortableCodeView -> Error NotPortablePdb
                | Some codeView ->
                    let data = pe.ReadCodeViewDebugDirectoryData codeView
                    let expected = BlobContentId(data.Guid, codeView.Stamp)
                    let sibling = Path.Combine(Path.GetDirectoryName(Path.GetFullPath assemblyPath), Path.GetFileName(data.Path.Replace('\\', '/')))
                    let candidates = [ sibling ]
                    let rec search mismatch remaining =
                        match remaining with
                        | [] ->
                            match mismatch with
                            | Some path -> Error(PdbMismatch path)
                            | None -> Error(PdbNotFound candidates)
                        | path :: rest when not (File.Exists path) -> search mismatch rest
                        | path :: rest ->
                            try
                                use pdb = File.OpenRead path
                                use provider = MetadataReaderProvider.FromPortablePdbStream(pdb, MetadataStreamOptions.PrefetchMetadata)
                                let reader = provider.GetMetadataReader()

                                if BlobContentId(reader.DebugMetadataHeader.Id) = expected then select prepared reader metadataToken
                                else search (Some path) rest
                            with
                            | :? BadImageFormatException as e -> Error(PdbMalformed(path, e.Message))
                            | :? IOException
                            | :? UnauthorizedAccessException as e -> Error(PdbUnreadable(path, e.Message))
                    search None candidates

    let private open' (assemblyPath: string) (metadataToken: int) prepare (select: 'p -> MetadataReader -> int -> Result<'a, TargetSourceAnchorError>) =
        if String.IsNullOrEmpty assemblyPath || not (File.Exists assemblyPath) then
            Error NoAssemblyFile
        else
            try
                read assemblyPath metadataToken prepare select
            with
            | :? BadImageFormatException as e -> Error(AssemblyMalformed(assemblyPath, e.Message))
            | :? IOException
            | :? UnauthorizedAccessException as e -> Error(AssemblyUnreadable(assemblyPath, e.Message))

    let tryFindIn (assemblyPath: string) (metadataToken: int) = open' assemblyPath metadataToken noFacts anchor

    let internal tryReadMethod (assemblyPath: string) (metadataToken: int) = open' assemblyPath metadataToken peFacts methodRecord

    let tryFind (method: MethodBase) =
        if method :? Emit.DynamicMethod || method.Module.Assembly.IsDynamic then Error NoAssemblyFile
        else tryFindIn method.Module.Assembly.Location method.MetadataToken
