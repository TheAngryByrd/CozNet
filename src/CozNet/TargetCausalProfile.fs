namespace CozNet

open System

type TargetCausalExperiment = {
    Block: int
    Order: int
    Candidate: string
    Speedup: uint32
    ElapsedNs: uint64
    GlobalNs: uint64
    EffectiveNs: uint64
    Passed: bool
    Error: string
}

type TargetCausalPoint = {
    Candidate: string
    Speedup: uint32
    Predictions: float option array
    ValidBlocks: int
    MedianPrediction: float option
}

type TargetCausalProfileResult = {
    Blocks: int
    Seed: int
    Candidates: string array
    Speedups: uint32 array
    Experiments: TargetCausalExperiment array
    Points: TargetCausalPoint array
}

type TargetCausalDesignError =
    | NoCandidates
    | DuplicateCandidate of symbol: string
    | UnboundCandidate of symbol: string
    | IneligibleCandidate of symbol: string
    | UnboundEligible of symbol: string
    | NoSpeedups
    | DuplicateSpeedup of speedup: uint32
    | SpeedupOutOfRange of speedup: uint32
    | NoBlocks of blocks: int
    | PlanRejected of message: string

type TargetCausalDesign = private {
    DesignBinding: CodeBinding
    DesignPeriod: uint64
    DesignEligible: string array
    DesignCandidates: string array
    DesignSpeedups: uint32 array
    DesignBlocks: int
    DesignSeed: int
} with
    member this.Binding = this.DesignBinding
    member this.Period = this.DesignPeriod
    member this.Eligible = Array.copy this.DesignEligible
    member this.Candidates = Array.copy this.DesignCandidates
    member this.Speedups = Array.copy this.DesignSpeedups
    member this.Blocks = this.DesignBlocks
    member this.Seed = this.DesignSeed

[<RequireQualifiedAccess>]
module TargetCausalDesign =
    let private duplicates (values: 'a array) =
        values |> Array.countBy id |> Array.filter (fun (_, count) -> count > 1) |> Array.map fst

    let tryCreate (binding: CodeBinding) (period: uint64) (eligible: string array) (candidates: string array) (speedups: uint32 array)
        (blocks: int) (seed: int) =
        let bound = binding.Symbols
        let errors =
            [ for symbol in eligible do
                  if not (Array.contains symbol bound) then UnboundEligible symbol
              if candidates.Length = 0 then NoCandidates
              for symbol in duplicates candidates do
                  DuplicateCandidate symbol
              for symbol in Array.distinct candidates do
                  if not (Array.contains symbol bound) then UnboundCandidate symbol
                  if not (Array.contains symbol eligible) then IneligibleCandidate symbol
              if speedups.Length = 0 then NoSpeedups
              for speedup in duplicates speedups do
                  DuplicateSpeedup speedup
              for speedup in Array.distinct speedups do
                  if speedup < 1u || speedup > 100u then SpeedupOutOfRange speedup
              if blocks < 1 then NoBlocks blocks ]

        match errors with
        | [] ->
            match TargetReplayPlan.tryCreate 0u period [| binding.Range candidates[0] |] (eligible |> Array.map binding.Range) with
            | Error message -> Error [ PlanRejected message ]
            | Ok _ ->
                Ok { DesignBinding = binding; DesignPeriod = period; DesignEligible = Array.copy eligible; DesignCandidates = Array.copy candidates
                     DesignSpeedups = Array.copy speedups; DesignBlocks = blocks; DesignSeed = seed }
        | errors -> Error errors

    let describe error =
        match error with
        | NoCandidates -> "The profile requires at least one candidate."
        | DuplicateCandidate symbol -> $"The candidate {symbol} occurs more than once."
        | UnboundCandidate symbol -> $"The candidate {symbol} is not a bound symbol."
        | IneligibleCandidate symbol -> $"The candidate {symbol} is not eligible."
        | UnboundEligible symbol -> $"The eligible symbol {symbol} is not a bound symbol."
        | NoSpeedups -> "The profile requires at least one speedup."
        | DuplicateSpeedup speedup -> $"The speedup {speedup} occurs more than once."
        | SpeedupOutOfRange speedup -> $"The speedup {speedup} is not from 1 to 100."
        | NoBlocks blocks -> $"The profile requires at least one block, but it has {blocks}."
        | PlanRejected message -> $"The replay plan rejects the design: {message}"

[<RequireQualifiedAccess>]
module TargetCausalProfile =
    let internal arms (random: Random) (candidates: string array) (speedups: uint32 array) =
        let arms = Array.append [| candidates[0], 0u |] [| for c in candidates do for s in speedups -> c, s |]
        for index in arms.Length - 1 .. -1 .. 1 do
            let other = random.Next(index + 1)
            let value = arms[index]
            arms[index] <- arms[other]
            arms[other] <- value
        arms

    let private median (values: float array) =
        let sorted = Array.sort values
        let middle = sorted.Length / 2
        if sorted.Length % 2 = 1 then sorted[middle] else (sorted[middle - 1] + sorted[middle]) / 2.0

    let internal points (blocks: int) (candidates: string array) (speedups: uint32 array) (experiments: TargetCausalExperiment array) =
        [| for candidate in candidates do
               for speedup in speedups do
                   let predictions =
                       Array.init blocks (fun index ->
                           let block = index + 1
                           let baseline = experiments |> Array.tryFind (fun e -> e.Block = block && e.Speedup = 0u)
                           let experiment = experiments |> Array.tryFind (fun e -> e.Block = block && e.Candidate = candidate && e.Speedup = speedup)
                           match baseline, experiment with
                           | Some b, Some e when b.Passed && e.Passed && b.EffectiveNs > 0UL ->
                               Some(1.0 - float e.EffectiveNs / float b.EffectiveNs)
                           | _ -> None)
                   let valid = predictions |> Array.choose id
                   { Candidate = candidate; Speedup = speedup; Predictions = predictions; ValidBlocks = valid.Length
                     MedianPrediction = if valid.Length = 0 then None else Some(median valid) } |]

    let internal failure (result: TargetProfileResult) =
        [ match result.BindingError with
          | Some message -> message
          | None -> ()
          match result.Replay with
          | Error message -> $"replay: {message}"
          | Ok summary ->
              if summary.GlobalDelay <> result.GlobalNs then $"replay global delay {summary.GlobalDelay} differs from native {result.GlobalNs}"
              if result.Experiment.Sequences <> uint64 result.Events.Length then
                  $"native sequences {result.Experiment.Sequences} differ from {result.Events.Length} events"
              if result.EffectiveNs = 0UL then "effective duration 0"
          if result.Experiment.Failure <> 0u then $"ledger failure {result.Experiment.Failure}"
          if result.CloseCode <> 0 then $"close {result.CloseCode}"
          if result.Experiment.ForeignSignals <> 0UL then $"foreign signals {result.Experiment.ForeignSignals}"
          if result.Experiment.SigioFallbacks <> 0UL then $"SIGIO fallbacks {result.Experiment.SigioFallbacks}"
          for index, thread in Array.indexed result.Threads do
              let r = thread.Result
              if thread.FinishCode <> 0 then $"thread {index}: finish {thread.FinishCode}"
              if thread.Error <> "" then $"thread {index}: {thread.Error}"
              if r.Complete <> 1 then $"thread {index}: incomplete epoch"
              if r.Lost <> 0UL then $"thread {index}: lost {r.Lost}"
              if r.Quiescent <> 1 then $"thread {index}: not quiescent"
              if r.MaxHandlerNs >= TargetLocalProfiler.handlerLimit then $"thread {index}: handler interval {r.MaxHandlerNs} ns" ]
        |> function
            | [] -> "The experiment did not pass."
            | parts -> String.Join("; ", parts)

    let internal runWith (runExperiment: string -> uint32 -> TargetProfileResult) (design: TargetCausalDesign) =
        let candidates, speedups, blocks, seed = design.Candidates, design.Speedups, design.Blocks, design.Seed
        let random = Random seed
        let experiments =
            [| for block in 1 .. blocks do
                   for order, (candidate, speedup) in arms random candidates speedups |> Array.indexed do
                       let result =
                           try
                               Ok(runExperiment candidate speedup)
                           with error ->
                               Error $"{error.GetType().Name}: {error.Message}"
                       match result with
                       | Ok r ->
                           { Block = block; Order = order; Candidate = candidate; Speedup = speedup; ElapsedNs = r.ElapsedNs; GlobalNs = r.GlobalNs
                             EffectiveNs = r.EffectiveNs; Passed = r.Passed
                             Error = if r.Passed then "" else failure r }
                       | Error message ->
                           { Block = block; Order = order; Candidate = candidate; Speedup = speedup; ElapsedNs = 0UL; GlobalNs = 0UL
                             EffectiveNs = 0UL; Passed = false; Error = message } |]
        { Blocks = blocks; Seed = seed; Candidates = candidates; Speedups = speedups; Experiments = experiments
          Points = points blocks candidates speedups experiments }

    let run (design: TargetCausalDesign) (journalCapacity: uint32) (workers: unit -> TargetWorker array) =
        let eligible = design.Eligible
        let experiment candidate speedup =
            GC.Collect(2, GCCollectionMode.Forced, true)
            TargetLocalProfiler.runBound design.Binding speedup design.Period [| candidate |] eligible journalCapacity (workers ()) ignore
        runWith experiment design
