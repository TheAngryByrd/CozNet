namespace CozNet

open System
open System.Threading

type TargetProgressPoint =
    | EndToEnd
    | Throughput of durationMs: int

[<Sealed; AllowNullLiteral>]
type TargetProgressCounter() =
    let mutable visits = 0L
    let mutable stopped = 0

    member _.Visit() = Interlocked.Increment(&visits) |> ignore
    member _.Visits = Interlocked.Read(&visits)
    member _.Stopped = Volatile.Read(&stopped) = 1
    member _.Stop() = Volatile.Write(&stopped, 1)

type TargetCausalThread = {
    CpuNs: uint64
    WallNs: uint64
    HandlerNs: uint64
    PaymentNs: uint64
    SkipNs: uint64
    Samples: int
    Wakes: int
    RunDelayNs: int64
}

type TargetCausalContextEvent = {
    Worker: int
    Kind: string
    ObservedGlobal: uint64
    LocalBefore: uint64
    Amount: uint64
    Requested: uint64
    Detail: uint32
    OffsetNs: int64
}

type TargetCausalWorkerContext = {
    Worker: int
    Events: int
    Children: int
    FailedChildren: int
    MeasuredNs: uint64
    RequestedNs: uint64
    MaxOvershootNs: Nullable<int64>
    MaxOvershootOffsetNs: Nullable<int64>
    Publishes: int
    LastPublishLocal: Nullable<uint64>
    LastPublishObserved: Nullable<uint64>
    LastPublishOffsetNs: Nullable<int64>
}

type TargetCausalFailure = {
    Worker: int
    Detail: uint32
    DebtNs: uint64
    TimeNs: Nullable<int64>
    GapNs: Nullable<int64>
    PreviousKind: string
    ContextEvents: int
    Context: TargetCausalContextEvent array
    Workers: TargetCausalWorkerContext array
}

type TargetCausalGc = { Collections: int; PauseNs: int64 }

type TargetCausalExperiment = {
    Block: int
    Order: int
    Candidate: string
    Speedup: uint32
    ElapsedNs: uint64
    GlobalNs: uint64
    EffectiveNs: uint64
    Visits: int64
    Threads: TargetCausalThread array
    Failures: TargetCausalFailure array
    Collections: int
    GcPauseNs: int64
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
    Progress: TargetProgressPoint
    Blocks: int
    Seed: int
    Candidates: string array
    Speedups: uint32 array
    Warmup: TargetCausalExperiment
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
    | InvalidDuration of durationMs: int
    | InvalidRotation of rotation: int
    | PlanRejected of message: string

type TargetCausalDesign = private {
    DesignBinding: CodeBinding
    DesignPeriod: uint64
    DesignEligible: string array
    DesignCandidates: string array
    DesignSpeedups: uint32 array
    DesignBlocks: int
    DesignSeed: int
    DesignRotation: int
    DesignProgress: TargetProgressPoint
} with
    member this.Binding = this.DesignBinding
    member this.Period = this.DesignPeriod
    member this.Eligible = Array.copy this.DesignEligible
    member this.Candidates = Array.copy this.DesignCandidates
    member this.Speedups = Array.copy this.DesignSpeedups
    member this.Blocks = this.DesignBlocks
    member this.Seed = this.DesignSeed
    member this.Rotation = this.DesignRotation
    member this.Progress = this.DesignProgress

[<RequireQualifiedAccess>]
module TargetCausalDesign =
    let internal maxDurationMs = 60000

    let private duplicates (values: 'a array) =
        values |> Array.countBy id |> Array.filter (fun (_, count) -> count > 1) |> Array.map fst

    let tryCreate (binding: CodeBinding) (period: uint64) (eligible: string array) (candidates: string array) (speedups: uint32 array)
        (blocks: int) (seed: int) (rotation: int) (progress: TargetProgressPoint) =
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
              if blocks < 1 then NoBlocks blocks
              if rotation < 0 || rotation > candidates.Length * speedups.Length then InvalidRotation rotation
              match progress with
              | Throughput duration when duration < 1 || duration > maxDurationMs -> InvalidDuration duration
              | _ -> () ]

        match errors with
        | [] ->
            match TargetReplayPlan.tryCreate 0u period [| binding.Range candidates[0] |] (eligible |> Array.map binding.Range) with
            | Error message -> Error [ PlanRejected message ]
            | Ok _ ->
                Ok { DesignBinding = binding; DesignPeriod = period; DesignEligible = Array.copy eligible; DesignCandidates = Array.copy candidates
                     DesignSpeedups = Array.copy speedups; DesignBlocks = blocks; DesignSeed = seed; DesignRotation = rotation; DesignProgress = progress }
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
        | InvalidRotation rotation -> $"The rotation {rotation} is not from 0 to the number of experiments in a block minus 1."
        | InvalidDuration duration -> $"A throughput experiment requires a duration from 1 to {maxDurationMs} ms, but it has {duration} ms."
        | PlanRejected message -> $"The replay plan rejects the design: {message}"

[<RequireQualifiedAccess>]
module TargetCausalProfile =
    let internal arms (random: Random) (candidates: string array) (speedups: uint32 array) : (string * uint32) array =
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
                           | Some b, Some e when b.Passed && e.Passed && b.EffectiveNs > 0UL && b.Visits > 0L && e.Visits > 0L ->
                               Some(1.0 - (float e.EffectiveNs / float e.Visits) / (float b.EffectiveNs / float b.Visits))
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

    let internal threads (result: TargetProfileResult) =
        let count = result.Threads.Length
        let payment, skip = Array.zeroCreate<uint64> count, Array.zeroCreate<uint64> count
        let samples, wakes = Array.zeroCreate<int> count, Array.zeroCreate<int> count
        for e in result.Events do
            if e.Worker >= 0 && e.Worker < count then
                match e.Kind with
                | TargetLedgerEventKind.Child -> payment[e.Worker] <- payment[e.Worker] + e.Amount
                | TargetLedgerEventKind.Wake ->
                    skip[e.Worker] <- skip[e.Worker] + e.Amount
                    wakes[e.Worker] <- wakes[e.Worker] + 1
                | TargetLedgerEventKind.Sample -> samples[e.Worker] <- samples[e.Worker] + 1
                | _ -> ()
        result.Threads
        |> Array.mapi (fun index outcome ->
            let ticks = outcome.FinishedTicks - outcome.StartedTicks
            { CpuNs = outcome.Result.Value
              WallNs = if outcome.StartedTicks = 0L || ticks < 0L then 0UL else uint64 (Diagnostics.Stopwatch.GetElapsedTime(outcome.StartedTicks, outcome.FinishedTicks).TotalNanoseconds)
              HandlerNs = outcome.Result.TotalHandlerNs
              PaymentNs = payment[index]
              SkipNs = skip[index]
              Samples = samples[index]
              Wakes = wakes[index]
              RunDelayNs = outcome.RunDelayNs })

    let internal contextWindowNs = 10_000_000L
    let internal contextLimit = 64

    let internal failures (result: TargetProfileResult) =
        let timed = result.Events |> Array.filter (fun e -> e.Time > 0UL)
        let origin = if timed.Length = 0 then 0UL else timed |> Array.minBy _.Time |> _.Time
        let last = Collections.Generic.Dictionary<int, TargetLedgerEvent>()
        let ordered = result.Events |> Array.sortBy _.Sequence
        let window (failure: TargetLedgerEvent) =
            if failure.Time = 0UL then Array.empty
            else ordered |> Array.filter (fun e -> e.Sequence < failure.Sequence && e.Time > 0UL && int64 e.Time - int64 failure.Time >= -contextWindowNs)
        let offset (failure: TargetLedgerEvent) (e: TargetLedgerEvent) = int64 e.Time - int64 failure.Time
        let context failure (events: TargetLedgerEvent array) =
            events[max 0 (events.Length - contextLimit) ..]
            |> Array.map (fun e -> { Worker = e.Worker; Kind = string e.Kind; ObservedGlobal = e.ObservedGlobal; LocalBefore = e.LocalBefore; Amount = e.Amount
                                     Requested = e.Requested; Detail = e.Detail; OffsetNs = offset failure e })
        let workers failure (events: TargetLedgerEvent array) =
            events
            |> Array.groupBy _.Worker
            |> Array.sortBy fst
            |> Array.map (fun (worker, mine) ->
                let children = mine |> Array.filter (fun e -> e.Kind = TargetLedgerEventKind.Child)
                let over = children |> Array.filter (fun e -> e.Detail = 0u) |> Array.sortBy (fun e -> -(int64 e.Amount - int64 e.Requested), e.Sequence) |> Array.tryHead
                let last = mine |> Array.filter (fun e -> e.Kind = TargetLedgerEventKind.Publish) |> Array.tryLast
                { Worker = worker; Events = mine.Length; Children = children.Length; FailedChildren = children |> Array.filter (fun e -> e.Detail <> 0u) |> Array.length
                  MeasuredNs = children |> Array.sumBy _.Amount; RequestedNs = children |> Array.sumBy _.Requested
                  MaxOvershootNs = (match over with Some e -> Nullable(int64 e.Amount - int64 e.Requested) | None -> Nullable())
                  MaxOvershootOffsetNs = (match over with Some e -> Nullable(offset failure e) | None -> Nullable())
                  Publishes = mine |> Array.filter (fun e -> e.Kind = TargetLedgerEventKind.Publish) |> Array.length
                  LastPublishLocal = (match last with Some e -> Nullable e.Amount | None -> Nullable())
                  LastPublishObserved = (match last with Some e -> Nullable e.ObservedGlobal | None -> Nullable())
                  LastPublishOffsetNs = (match last with Some e -> Nullable(offset failure e) | None -> Nullable()) })
        [| for e in ordered do
               if e.Kind = TargetLedgerEventKind.Failure then
                   let events = window e
                   let previous = if e.Time = 0UL then None else match last.TryGetValue e.Worker with | true, p -> Some p | _ -> None
                   { Worker = e.Worker; Detail = e.Detail; DebtNs = e.Amount
                     TimeNs = (if e.Time = 0UL then Nullable() else Nullable(int64 (e.Time - origin)))
                     GapNs = (match previous with Some p -> Nullable(int64 e.Time - int64 p.Time) | None -> Nullable())
                     PreviousKind = (match previous with Some p -> string p.Kind | None -> "")
                     ContextEvents = events.Length
                     Context = context e events
                     Workers = workers e events }
               if e.Time > 0UL then last[e.Worker] <- e |]

    let internal runWith (runExperiment: string -> uint32 -> TargetProfileResult * int64 * string option * TargetCausalGc) (design: TargetCausalDesign) =
        let candidates, speedups, blocks, seed = design.Candidates, design.Speedups, design.Blocks, design.Seed
        let order = arms (Random seed) candidates speedups

        let measure block order candidate speedup =
            let result =
                try
                    Ok(runExperiment candidate speedup)
                with error ->
                    Error $"{error.GetType().Name}: {error.Message}"

            match result with
            | Ok(r, visits, stop, gc) ->
                let errors = [ if not r.Passed then failure r
                               match stop with
                               | Some message -> message
                               | None -> () ]
                { Block = block; Order = order; Candidate = candidate; Speedup = speedup; ElapsedNs = r.ElapsedNs; GlobalNs = r.GlobalNs
                  EffectiveNs = r.EffectiveNs; Visits = visits; Threads = threads r; Failures = failures r; Collections = gc.Collections
                  GcPauseNs = gc.PauseNs; Passed = r.Passed && stop.IsNone
                  Error = String.Join("; ", errors) }
            | Error message ->
                { Block = block; Order = order; Candidate = candidate; Speedup = speedup; ElapsedNs = 0UL; GlobalNs = 0UL
                  EffectiveNs = 0UL; Visits = 0L; Threads = Array.empty; Failures = Array.empty; Collections = -1; GcPauseNs = -1L
                  Passed = false; Error = message }

        let warmup = measure 0 -1 candidates[0] 0u
        let experiments =
            [| for block in 1 .. blocks do
                   let shift = design.Rotation + block - 1
                   for position, (candidate, speedup) in Array.init order.Length (fun i -> order[(i + shift) % order.Length]) |> Array.indexed do
                       measure block position candidate speedup |]
        { Progress = design.Progress; Blocks = blocks; Seed = seed; Candidates = candidates; Speedups = speedups; Warmup = warmup
          Experiments = experiments; Points = points blocks candidates speedups experiments }

    let internal stopAfter (duration: int) (counter: TargetProgressCounter) (working: unit -> bool) (running: unit -> bool) =
        try
            while not (working ()) && running () do
                Thread.SpinWait 100
            if working () then
                Thread.Sleep duration
        finally
            counter.Stop()

    let internal safetyDelay (duration: int) = TimeSpan.FromMilliseconds(1000.0 + 10.0 * float duration)

    let internal guard (delay: TimeSpan) (counter: TargetProgressCounter) (action: unit -> 'a) =
        use finished = new ManualResetEventSlim()
        let fired = [| 0 |]
        let watcher =
            Thread((fun () ->
                if not (finished.Wait delay) then
                    Volatile.Write(&fired[0], 1)
                    counter.Stop()), IsBackground = true)
        watcher.Start()
        let mutable interrupted = false
        let value =
            try
                action ()
            finally
                finished.Set()
                let mutable joined = false
                while not joined do
                    try
                        watcher.Join()
                        joined <- true
                    with :? ThreadInterruptedException ->
                        interrupted <- true
        if interrupted then
            raise (ThreadInterruptedException "The calling thread was interrupted while it joined the safety watcher.")
        value, Volatile.Read(&fired[0]) = 1

    let run (design: TargetCausalDesign) (journalCapacity: uint32) (workers: TargetProgressCounter -> TargetWorker array) =
        let eligible = design.Eligible
        let experiment candidate speedup =
            let counter = TargetProgressCounter()
            let monitor (state: TargetRunState) =
                match design.Progress with
                | EndToEnd -> ()
                | Throughput duration -> stopAfter duration counter (fun () -> state.Working) (fun () -> state.Running)
            let built = workers counter
            GC.Collect(2, GCCollectionMode.Forced, true)
            let collections, pause = GC.CollectionCount 0, GC.GetTotalPauseDuration()
            let gc () = { Collections = GC.CollectionCount 0 - collections; PauseNs = int64 (GC.GetTotalPauseDuration() - pause).TotalNanoseconds }
            let profile () = TargetLocalProfiler.runBound design.Binding speedup design.Period [| candidate |] eligible journalCapacity built monitor
            match design.Progress with
            | EndToEnd ->
                let result = profile ()
                result, 1L, None, gc ()
            | Throughput duration ->
                let result, fired = guard (safetyDelay duration) counter profile
                result, counter.Visits, (if fired then Some $"The safety stop ended the run after {safetyDelay duration}." else None), gc ()
        runWith experiment design
