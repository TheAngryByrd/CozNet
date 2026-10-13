open System
open System.Diagnostics
open System.Globalization
open System.IO
open System.Runtime.CompilerServices
open System.Text.Json
open CozNet

let parsed value = Result.defaultWith failwith value

let target =
    Region.tryCreate "redundant-checksum"
    |> parsed

let other =
    Region.tryCreate "other-checksum"
    |> parsed

let count = 2000000

[<MethodImpl(MethodImplOptions.NoInlining)>]
let checksum count =
    let mutable value = 17UL

    for index in 1..count do
        value <-
            (value
             ^^^ uint64 index)
            * 1099511628211UL

    value

let expected = checksum count

let verify () =
    if
        checksum count
        <> expected
    then
        failwith "The workload output changed."

let work passes (session: Session) =
    session.Region(
        target,
        fun () ->
            for _ in 1..passes do
                verify ()
    )

    session.Region(
        other,
        fun () ->
            for _ in 1..4 do
                verify ()
    )

    session.Progress()

let measure passes =
    let timer = Stopwatch.StartNew()

    for _ in 1..passes do
        verify ()

    for _ in 1..4 do
        verify ()

    timer.Stop()
    timer.Elapsed.TotalSeconds

let median values =
    let sorted = Array.sort values

    sorted.[sorted.Length
            / 2]

[<EntryPoint>]
let main args =
    if args.Length > 1 then
        eprintfn "Usage: CozNet.Serial [output-directory]"
        2
    else
        let output = if args.Length = 1 then args.[0] else "artifacts/serial"

        Directory.CreateDirectory output
        |> ignore

        let speeds =
            [
                0.0
                0.25
                0.5
            ]
            |> List.map (
                VirtualSpeedup.tryCreate
                >> parsed
            )

        let settings =
            ExperimentSettings.tryCreate target speeds 21 5 1729
            |> parsed

        let result = Experiment.run settings (work 4)
        let random = Random 2718

        let actual = [
            for speed, passes in
                [
                    0.0, 4
                    0.25, 3
                    0.5, 2
                ] do
                let pairs = [|
                    for block in 0..20 do
                        let baselineFirst = random.Next(2) = 0

                        let baseline, changed =
                            if baselineFirst then
                                let baseline = measure 4
                                baseline, measure passes
                            else
                                let changed = measure passes
                                measure 4, changed

                        yield {|
                            block = block
                            baselineFirst = baselineFirst
                            baselineSeconds = baseline
                            changedSeconds = changed
                            reduction =
                                1.0
                                - changed
                                  / baseline
                        |}
                |]

                yield speed, pairs
        ]

        let rows =
            result.Observations
            |> List.map (fun item ->
                let kind, speed =
                    match item.Intervention with
                    | Intervention.Control s -> "control", s
                    | Intervention.Treatment s -> "treatment", s

                let undelayed =
                    item.Elapsed.TotalSeconds
                    - item.InjectedDelay.TotalSeconds

                {|
                    block = item.Block
                    order = item.Order
                    intervention = kind
                    speedup = VirtualSpeedup.fraction speed
                    elapsedSeconds = item.Elapsed.TotalSeconds
                    regionSeconds = item.RegionTime.TotalSeconds
                    targetSeconds = item.TargetTime.TotalSeconds
                    injectedDelaySeconds = item.InjectedDelay.TotalSeconds
                    progress = item.Progress
                    coverage =
                        item.RegionTime.TotalSeconds
                        / undelayed
                    gcCollections = item.GcCollections
                |}
            )

        let comparisons =
            result.Estimates
            |> List.map (fun estimate ->
                let speed = VirtualSpeedup.fraction estimate.Speedup

                let pairs =
                    actual
                    |> List.find (fun (s, _) -> s = speed)
                    |> snd

                let measured =
                    pairs
                    |> Array.map (fun p -> p.reduction)
                    |> median

                {|
                    speedup = speed
                    predictedReduction = estimate.MedianElapsedReduction
                    measuredReduction = measured
                    errorPercentagePoints =
                        100.0
                        * abs (
                            estimate.MedianElapsedReduction
                            - measured
                        )
                    minimumBlockReduction = estimate.MinimumElapsedReduction
                    maximumBlockReduction = estimate.MaximumElapsedReduction
                    actualPairs = pairs
                |}
            )

        let artifact = {|
            runtime = result.Runtime
            operatingSystem = result.OperatingSystem
            processorCount = result.ProcessorCount
            timestampFrequency = result.TimestampFrequency
            seed = 1729
            actualSeed = 2718
            blocks = 21
            warmupRuns = 5
            checksumIterations = count
            expectedChecksum = expected.ToString(CultureInfo.InvariantCulture)
            observations = rows
            comparisons = comparisons
        |}

        let path = Path.Combine(output, "measurements.json")

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(artifact, JsonSerializerOptions(WriteIndented = true))
        )

        for comparison in comparisons do
            printfn
                "Region reduction %.0f%%: predicted %.2f%%, measured %.2f%%, error %.2f percentage points"
                (comparison.speedup
                 * 100.0)
                (comparison.predictedReduction
                 * 100.0)
                (comparison.measuredReduction
                 * 100.0)
                comparison.errorPercentagePoints

        printfn "Raw observations: %s" path
        printfn "These are serial workload observations, not a general accuracy guarantee."

        if
            comparisons
            |> List.exists (fun c -> c.errorPercentagePoints > 2.0)
        then
            1
        else
            0
