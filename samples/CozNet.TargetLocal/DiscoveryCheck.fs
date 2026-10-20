module CozNet.TargetLocal.DiscoveryCheck

open System
open System.Reflection
open CozNet

let scopePrefix = "CozNet.TargetLocal."
let rankLimit = 8
let designSpeedup = 50u

type AnchorRecord = {
    symbol: string
    found: bool
    document: string
    line: int
    column: int
    error: string
}

type MethodRecord = {
    symbol: string
    assembly: string
    start: uint64
    ``end``: uint64
    samples: int64
    workerSamples: int64 array array
}

type CountsRecord = {
    samples: int64
    unmapped: int64
    notMethodLabel: int64
    notOptimized: int64
    notUnique: int64
    outOfScope: int64
    inScope: int64
    omittedMethods: int
    omittedSamples: int64
}

type Evaluation = {
    sampleEvents: int
    rankError: string
    counts: CountsRecord option
    ranked: MethodRecord array
    anchors: AnchorRecord array
    bindingError: string
    boundRanges: uint64 array array
    designErrors: string array
    designValid: bool
}

let methodsBySymbol (assembly: Assembly) =
    assembly.GetTypes()
    |> Array.collect (fun t -> t.GetMethods(BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Static ||| BindingFlags.Instance ||| BindingFlags.DeclaredOnly))
    |> Array.choose (fun m -> match TargetSymbol.tryFormat m with Ok symbol -> Some(symbol, m) | Error _ -> None)
    |> Array.groupBy fst
    |> Array.choose (fun (symbol, matches) -> if matches.Length = 1 then Some(symbol, snd matches[0]) else None)
    |> Map.ofArray

let private anchorOf (methods: Map<string, MethodInfo>) (symbol: string) =
    let record found document line column error = { symbol = symbol; found = found; document = document; line = line; column = column; error = error }

    match methods.TryFind symbol with
    | None -> record false "" 0 0 "no method of the sample assembly formats as this symbol"
    | Some method ->
        match TargetSourceAnchor.tryFind method with
        | Ok anchor -> record true anchor.Document anchor.Line anchor.Column ""
        | Error error -> record false "" 0 0 (sprintf "%A" error)

let evaluate (lines: string array) (run: TargetProfileResult) (methods: Map<string, MethodInfo>) (period: uint64) =
    let sampleEvents = run.Events |> Array.filter (fun e -> e.Kind = TargetLedgerEventKind.Sample) |> Array.length
    let empty = { sampleEvents = sampleEvents; rankError = ""; counts = None; ranked = [||]; anchors = [||]; bindingError = ""; boundRanges = [||]; designErrors = [||]; designValid = false }
    let ranking =
        match PerfMapIndex.tryCreate lines, TargetDiscoveryScope.tryCreate [ scopePrefix ] [], TargetDiscoveryLimit.tryCreate rankLimit with
        | Error error, _, _ -> Error(PerfMapIndex.describe error)
        | _, Error error, _ -> Error(sprintf "%A" error)
        | _, _, Error message -> Error message
        | Ok index, Ok scope, Ok limit -> TargetDiscovery.rank scope index run limit |> Result.mapError (sprintf "%A")

    match ranking with
    | Error message -> { empty with rankError = message }
    | Ok result ->
        let c = result.Counts
        let counts = { samples = c.Samples; unmapped = c.Unmapped; notMethodLabel = c.NotBindable.NotMethodLabel; notOptimized = c.NotBindable.NotOptimized
                       notUnique = c.NotBindable.NotUnique; outOfScope = c.OutOfScope; inScope = c.InScope
                       omittedMethods = result.OmittedMethods; omittedSamples = result.OmittedSamples }
        let ranked =
            result.Ranked
            |> Array.map (fun m -> { symbol = m.Symbol; assembly = m.Assembly; start = m.Start; ``end`` = m.End; samples = m.Samples
                                     workerSamples = m.WorkerSamples |> Array.map (fun (worker, count) -> [| int64 worker; count |]) })
        let symbols = result.Ranked |> Array.map _.Symbol
        let anchors = symbols |> Array.map (anchorOf methods)

        match CodeBinding.tryCapture lines symbols with
        | Error message -> { empty with counts = Some counts; ranked = ranked; anchors = anchors; bindingError = message }
        | Ok binding ->
            let ranges = symbols |> Array.map (fun s -> let first, last = binding.Range s in [| first; last |])

            match TargetCausalDesign.tryCreate binding period symbols symbols [| designSpeedup |] 1 0 0 EndToEnd with
            | Ok _ -> { empty with counts = Some counts; ranked = ranked; anchors = anchors; boundRanges = ranges; designValid = true }
            | Error errors ->
                { empty with counts = Some counts; ranked = ranked; anchors = anchors; boundRanges = ranges
                             designErrors = errors |> List.map TargetCausalDesign.describe |> List.toArray }
