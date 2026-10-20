module CozNet.TargetLocal.DiscoveryCheck

open CozNet

let schema = "coznet.discovery-check/2"
let mapHashKind = "FileBytesSha256"
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
    eligibility: string
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
    outsideBinding: string array
    anchors: AnchorRecord array
    bindingError: string
    boundRanges: uint64 array array
    designErrors: string array
    designEligible: string array
    designCandidates: string array
    designValid: bool
    limitations: string array
}

let private anchorRecord (candidate: TargetDiscoveredCandidate) =
    let record found document line column error = { symbol = candidate.Method.Symbol; found = found; document = document; line = line; column = column; error = error }

    match candidate.Anchor with
    | Anchored anchor -> record true anchor.Document anchor.Line anchor.Column ""
    | other -> record false "" 0 0 (sprintf "%A" other)

let evaluate (map: PerfMapSnapshot) (eligible: CodeBinding) (run: TargetProfileResult) (catalog: TargetMethodCatalog option) (period: uint64) =
    let sampleEvents = run.Events |> Array.filter (fun e -> e.Kind = TargetLedgerEventKind.Sample) |> Array.length
    let empty = { sampleEvents = sampleEvents; rankError = ""; counts = None; ranked = [||]; outsideBinding = [||]; anchors = [||]; bindingError = ""
                  boundRanges = [||]; designErrors = [||]; designValid = false; designEligible = [||]; designCandidates = [||]; limitations = [||] }
    let outcome =
        match TargetDiscoveryScope.tryCreate [ scopePrefix ] [], TargetDiscoveryLimit.tryCreate rankLimit with
        | Error error, _ -> Error(sprintf "%A" error)
        | _, Error message -> Error message
        | Ok scope, Ok limit -> TargetDiscoveryWorkflow.prepare scope eligible run map catalog limit |> Result.mapError (sprintf "%A")

    match outcome with
    | Error message -> { empty with rankError = message }
    | Ok outcome ->
        let c = outcome.Counts
        let counts = { samples = c.Samples; unmapped = c.Unmapped; notMethodLabel = c.NotBindable.NotMethodLabel; notOptimized = c.NotBindable.NotOptimized
                       notUnique = c.NotBindable.NotUnique; outOfScope = c.OutOfScope; inScope = c.InScope
                       omittedMethods = outcome.OmittedMethods; omittedSamples = outcome.OmittedSamples }
        let candidates = outcome.Candidates
        let ranked =
            candidates
            |> Array.map (fun candidate ->
                let m = candidate.Method
                { symbol = m.Symbol; assembly = m.Assembly; start = m.Start; ``end`` = m.End; samples = m.Samples
                  workerSamples = m.WorkerSamples |> Array.map (fun (worker, count) -> [| int64 worker; count |])
                  eligibility = sprintf "%A" candidate.Eligibility })
        let symbols = candidates |> Array.map _.Method.Symbol
        let inBinding = candidates |> Array.filter (fun c -> c.Eligibility = InEligibleBinding) |> Array.map _.Method.Symbol
        let evaluated = { empty with counts = Some counts; ranked = ranked; anchors = candidates |> Array.map anchorRecord
                                     outsideBinding = candidates |> Array.filter (fun c -> c.Eligibility = OutsideEligibleBinding) |> Array.map _.Method.Symbol
                                     limitations = outcome.Limitations |> List.map (sprintf "%A") |> List.toArray }
        let evaluated =
            match CodeBinding.tryCapture map.Lines symbols with
            | Error message -> { evaluated with bindingError = message }
            | Ok binding -> { evaluated with boundRanges = symbols |> Array.map (fun s -> let first, last = binding.Range s in [| first; last |]) }

        match TargetDiscoveryWorkflow.tryDesign outcome eligible.Symbols inBinding period [| designSpeedup |] 1 0 0 EndToEnd with
        | Ok design -> { evaluated with designValid = true; designEligible = design.Eligible; designCandidates = design.Candidates }
        | Error errors -> { evaluated with designErrors = errors |> List.map (sprintf "%A") |> List.toArray }
