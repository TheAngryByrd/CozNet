namespace CozNet

open System
open System.Diagnostics
open System.Runtime.InteropServices
open System.Threading

type internal ITiming =
    abstract Timestamp: unit -> int64
    abstract Frequency: int64
    abstract Delay: int64 -> unit

type internal StopwatchTiming() =
    interface ITiming with
        member _.Timestamp() = Stopwatch.GetTimestamp()
        member _.Frequency = Stopwatch.Frequency

        member _.Delay ticks =
            let start = Stopwatch.GetTimestamp()

            while Stopwatch.GetTimestamp()
                  - start < ticks do
                Thread.SpinWait 16

type Session internal (target: Region, intervention: Intervention, timing: ITiming) =
    let owner = Environment.CurrentManagedThreadId
    let start = timing.Timestamp()
    let collections = [ for generation in 0..2 -> GC.CollectionCount generation ]
    let mutable closed = false
    let mutable insideRegion = false
    let mutable invalid = 0
    let mutable regionTicks = 0L
    let mutable targetTicks = 0L
    let mutable injectedTicks = 0L
    let mutable progress = 0L
    let mutable calls = 0L
    let mutable targetCalls = 0L

    let reject message =
        Interlocked.Exchange(&invalid, 1)
        |> ignore

        invalidOp message

    let check () =
        if
            Environment.CurrentManagedThreadId
            <> owner
        then
            reject "A session supports only its experiment thread."

        if closed then
            reject "The experiment session is closed."

        if
            Volatile.Read(&invalid)
            <> 0
        then
            invalidOp "The experiment session is invalid."

    let duration ticks =
        TimeSpan.FromTicks(
            int64 (
                Math.Round(
                    float ticks
                    * (float TimeSpan.TicksPerSecond
                       / float timing.Frequency)
                )
            )
        )

    member _.Region(region, work: unit -> unit) =
        check ()

        if insideRegion then
            reject "Nested regions are not supported."

        insideRegion <- true

        try
            try
                let before = timing.Timestamp()
                work ()
                check ()

                let elapsed =
                    timing.Timestamp()
                    - before

                regionTicks <- Checked.(+) regionTicks elapsed
                calls <- Checked.(+) calls 1L

                if region = target then
                    targetTicks <- Checked.(+) targetTicks elapsed
                    targetCalls <- Checked.(+) targetCalls 1L

                let speedup, applyDelay =
                    match intervention with
                    | Intervention.Control speedup -> speedup, true
                    | Intervention.Treatment speedup ->
                        speedup,
                        region
                        <> target

                if applyDelay then
                    let fraction = VirtualSpeedup.fraction speedup

                    let requested =
                        Math.Ceiling(
                            float elapsed
                            * fraction
                            / (1.0
                               - fraction)
                        )

                    if
                        requested
                        >= float Int64.MaxValue
                    then
                        reject "The requested delay is too large."

                    if requested > 0.0 then
                        let delayStart = timing.Timestamp()
                        timing.Delay(int64 requested)

                        injectedTicks <-
                            Checked.(+)
                                injectedTicks
                                (timing.Timestamp()
                                 - delayStart)
            with _ ->
                Interlocked.Exchange(&invalid, 1)
                |> ignore

                reraise ()
        finally
            insideRegion <- false

    member _.Progress() =
        check ()

        if insideRegion then
            reject "Record progress after leaving the region."

        progress <- Checked.(+) progress 1L

    member internal _.Finish(block, order) =
        check ()

        if progress = 0L then
            reject "A measured run must record progress."

        if targetCalls = 0L then
            reject "A measured run must execute the selected region."

        let elapsed =
            timing.Timestamp()
            - start

        if
            elapsed
            <= 0L
        then
            reject "A measured run must have positive elapsed time."

        {
            Block = block
            Order = order
            Intervention = intervention
            Elapsed = duration elapsed
            RegionTime = duration regionTicks
            TargetTime = duration targetTicks
            InjectedDelay = duration injectedTicks
            Progress = progress
            RegionCalls = calls
            TargetCalls = targetCalls
            GcCollections =
                collections
                |> List.mapi (fun generation count ->
                    GC.CollectionCount generation
                    - count
                )
        }

    member internal _.Close() = closed <- true

[<RequireQualifiedAccess>]
module Experiment =
    let private median values =
        let sorted = Array.sort values

        let middle =
            sorted.Length
            / 2

        if sorted.Length % 2 = 0 then
            (sorted.[middle - 1]
             + sorted.[middle])
            / 2.0
        else
            sorted.[middle]

    let internal runWith (timing: ITiming) settings (work: Session -> unit) =
        let target = ExperimentSettings.target settings

        let run block order intervention =
            let session = Session(target, intervention, timing)

            try
                work session
                session.Finish(block, order)
            finally
                session.Close()

        let zero =
            VirtualSpeedup.tryCreate 0.0
            |> Result.defaultWith invalidOp

        for _ in 1 .. ExperimentSettings.warmupRuns settings do
            run -1 -1 (Intervention.Control zero)
            |> ignore

        let random = Random(ExperimentSettings.seed settings)
        let observations = ResizeArray<Observation>()

        for block in
            0 .. ExperimentSettings.blocks settings
                 - 1 do
            let treatments =
                ExperimentSettings.speedups settings
                |> List.collect (fun speedup -> [
                    Intervention.Control speedup
                    Intervention.Treatment speedup
                ])
                |> List.toArray

            for index in
                treatments.Length
                - 1 .. -1 .. 1 do
                let other = random.Next(index + 1)
                let previous = treatments.[index]
                treatments.[index] <- treatments.[other]
                treatments.[other] <- previous

            for order in
                0 .. treatments.Length
                     - 1 do
                let observation = run block order treatments.[order]

                if
                    observations.Count > 0
                    && observation.Progress
                       <> observations.[0].Progress
                then
                    invalidOp "Every measured run must complete the same amount of progress."

                observations.Add observation

        let estimates =
            let indexed =
                observations
                |> Seq.map (fun item -> (item.Block, item.Intervention), item)
                |> Map.ofSeq

            ExperimentSettings.speedups settings
            |> List.map (fun speedup ->
                let reductions = [|
                    for block in
                        0 .. ExperimentSettings.blocks settings
                             - 1 do
                        let control = indexed.[block, Intervention.Control speedup]
                        let treatment = indexed.[block, Intervention.Treatment speedup]

                        yield
                            1.0
                            - treatment.Elapsed.TotalSeconds
                              / control.Elapsed.TotalSeconds
                |]

                {
                    Speedup = speedup
                    MedianElapsedReduction = median reductions
                    MinimumElapsedReduction = Array.min reductions
                    MaximumElapsedReduction = Array.max reductions
                    Blocks = reductions.Length
                }
            )

        {
            Settings = settings
            Observations = List.ofSeq observations
            Estimates = estimates
            Runtime = RuntimeInformation.FrameworkDescription
            OperatingSystem = RuntimeInformation.OSDescription
            ProcessorCount = Environment.ProcessorCount
            TimestampFrequency = timing.Frequency
        }

    let run settings work =
        runWith (StopwatchTiming()) settings work
