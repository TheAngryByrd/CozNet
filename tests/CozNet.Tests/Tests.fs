namespace CozNet.Tests

open System
open System.Threading
open CozNet
open Expecto

type private Clock() =
    let mutable ticks = 0L

    member _.Advance amount =
        ticks <-
            ticks
            + amount

    interface ITiming with
        member _.Timestamp() = ticks
        member _.Frequency = 1000000L

        member _.Delay amount =
            ticks <-
                ticks
                + amount

module Tests =
    let private parsed value = Result.defaultWith failwith value

    let private target =
        Region.tryCreate "target"
        |> parsed

    let private other =
        Region.tryCreate "other"
        |> parsed

    let private speedup value =
        VirtualSpeedup.tryCreate value
        |> parsed

    let private settings values blocks warmup seed =
        ExperimentSettings.tryCreate target (List.map speedup values) blocks warmup seed
        |> parsed

    let private workload (clock: Clock) (session: Session) =
        session.Region(target, fun () -> clock.Advance 100L)
        session.Region(other, fun () -> clock.Advance 100L)
        session.Progress()

    let private run values blocks seed =
        let clock = Clock()
        Experiment.runWith clock (settings values blocks 0 seed) (workload clock)

    let private near expected actual =
        Expect.isTrue
            (abs (
                expected
                - actual
            ) < 0.0000001)
            $"Expected {expected}, got {actual}."

    [<Tests>]
    let domain =
        testList "Domain" [
            testCase "Region names reject blank input"
            <| fun _ ->
                for value in
                    [
                        null
                        ""
                        "  "
                    ] do
                    Expect.isError (Region.tryCreate value) "Blank names must fail."

                Expect.equal
                    (Region.tryCreate " CPU "
                     |> parsed
                     |> Region.name)
                    " CPU "
                    "Names are not normalized."
            testCase "Speedup bounds reject nonfinite values"
            <| fun _ ->
                for value in
                    [
                        Double.NaN
                        Double.PositiveInfinity
                        Double.NegativeInfinity
                        -0.001
                        0.901
                    ] do
                    Expect.isError (VirtualSpeedup.tryCreate value) $"Reject {value}."

                for value in
                    [
                        0.0
                        0.5
                        0.9
                    ] do
                    Expect.equal
                        (speedup value
                         |> VirtualSpeedup.fraction)
                        value
                        "Accept the documented interval."
            testCase "Settings require bounded counts and distinct speedups"
            <| fun _ ->
                let make values blocks warmup =
                    ExperimentSettings.tryCreate target values blocks warmup 1

                for values, blocks, warmup in
                    [
                        [], 3, 0
                        [
                            speedup 0.0
                            speedup 0.0
                        ],
                        3,
                        0
                        [ speedup 0.5 ], 2, 0
                        [ speedup 0.5 ], 10001, 0
                        [ speedup 0.5 ], 3, -1
                        [ speedup 0.5 ], 3, 101
                        [
                            for i in 0..16 ->
                                speedup (
                                    float i
                                    / 100.0
                                )
                        ],
                        3,
                        0
                    ] do
                    Expect.isError
                        (make values blocks warmup)
                        "Invalid settings must not reach the runner."

                for blocks, warmup in
                    [
                        3, 0
                        10000, 100
                    ] do
                    Expect.isOk (make [ speedup 0.5 ] blocks warmup) "Accept valid endpoints."
        ]

    [<Tests>]
    let experiments =
        testList "Experiment" [
            testCase "Serial intervention predicts known work reduction"
            <| fun _ ->
                let result = run [ 0.5 ] 3 42
                Expect.equal result.Observations.Length 6 "Three complete pairs."
                near 0.25 result.Estimates.Head.MedianElapsedReduction

                for item in result.Observations do
                    near 0.0002 item.RegionTime.TotalSeconds
                    near 0.0001 item.TargetTime.TotalSeconds
                    Expect.equal item.Progress 1L "Progress is independent of region calls."
                    Expect.equal item.RegionCalls 2L "Both regions execute."
                    Expect.equal item.TargetCalls 1L "The selected region executes once."

                    match item.Intervention with
                    | Intervention.Control _ ->
                        near 0.0004 item.Elapsed.TotalSeconds
                        near 0.0002 item.InjectedDelay.TotalSeconds
                    | Intervention.Treatment _ ->
                        near 0.0003 item.Elapsed.TotalSeconds
                        near 0.0001 item.InjectedDelay.TotalSeconds
            testCase "Zero intervention predicts no gain"
            <| fun _ ->
                let result = run [ 0.0 ] 4 7
                near 0.0 result.Estimates.Head.MedianElapsedReduction
                near 0.0 result.Estimates.Head.MinimumElapsedReduction
                near 0.0 result.Estimates.Head.MaximumElapsedReduction

                for item in result.Observations do
                    Expect.equal
                        item.InjectedDelay
                        TimeSpan.Zero
                        "A null intervention does not wait."

                    near 0.0002 item.Elapsed.TotalSeconds
            testCase "Randomized blocks retain every pair and replay the seed"
            <| fun _ ->
                let order seed =
                    (run
                        [
                            0.0
                            0.25
                            0.5
                        ]
                        4
                        seed)
                        .Observations
                    |> List.map (fun o -> o.Block, o.Order, o.Intervention)

                Expect.equal (order 21) (order 21) "A seed repeats treatment order."
                Expect.notEqual (order 21) (order 99) "Distinct seeds vary this schedule."

                for _, block in
                    order 21
                    |> List.groupBy (fun (block, _, _) -> block) do
                    Expect.equal
                        (block
                         |> List.map (fun (_, index, _) -> index))
                        [ 0..5 ]
                        "Order is recorded."

                    let interventions =
                        block
                        |> List.map (fun (_, _, intervention) -> intervention)
                        |> Set.ofList

                    let expected =
                        [
                            for v in
                                [
                                    0.0
                                    0.25
                                    0.5
                                ] do
                                yield Intervention.Control(speedup v)
                                yield Intervention.Treatment(speedup v)
                        ]
                        |> Set.ofList

                    Expect.equal interventions expected "Every block contains each condition once."
            testCase "Warmups do not enter observations"
            <| fun _ ->
                let clock = Clock()
                let mutable runs = 0

                let result =
                    Experiment.runWith
                        clock
                        (settings [ 0.5 ] 3 2 1)
                        (fun s ->
                            runs <- runs + 1
                            workload clock s
                        )

                Expect.equal runs 8 "Two warmups plus six measurements."
                Expect.equal result.Observations.Length 6 "Warmups stay out of the estimator."
                near 0.25 result.Estimates.Head.MedianElapsedReduction
            testCase "Median uses both middle blocks"
            <| fun _ ->
                let clock = Clock()
                let mutable invocation = 0

                let costs = [|
                    100L
                    200L
                    300L
                    400L
                |]

                let result =
                    Experiment.runWith
                        clock
                        (settings [ 0.5 ] 4 0 1)
                        (fun s ->
                            let cost =
                                costs.[invocation
                                       / 2]

                            invocation <-
                                invocation
                                + 1

                            s.Region(target, fun () -> clock.Advance cost)
                            s.Region(other, fun () -> clock.Advance 100L)
                            s.Progress()
                        )

                near
                    ((1.0 / 3.0
                      + 0.375)
                     / 2.0)
                    result.Estimates.Head.MedianElapsedReduction

                near 0.25 result.Estimates.Head.MinimumElapsedReduction
                near 0.4 result.Estimates.Head.MaximumElapsedReduction
        ]

    [<Tests>]
    let safety =
        testList "Session" [
            testCase "Caught nesting still invalidates the run"
            <| fun _ ->
                let clock = Clock()

                let work (s: Session) =
                    s.Region(
                        target,
                        fun () ->
                            try
                                s.Region(other, ignore)
                            with :? InvalidOperationException ->
                                ()

                            clock.Advance 100L
                    )

                    s.Progress()

                Expect.throwsT<InvalidOperationException>
                    (fun () ->
                        Experiment.runWith clock (settings [ 0.5 ] 3 0 1) work
                        |> ignore
                    )
                    "Reject invalid measurements."
            testCase "Cross thread use invalidates the run"
            <| fun _ ->
                let clock = Clock()
                let mutable rejected = false

                let work (s: Session) =
                    let thread =
                        Thread(
                            ThreadStart(fun () ->
                                try
                                    s.Region(target, ignore)
                                with :? InvalidOperationException ->
                                    rejected <- true
                            )
                        )

                    thread.Start()
                    thread.Join()
                    workload clock s

                Expect.throwsT<InvalidOperationException>
                    (fun () ->
                        Experiment.runWith clock (settings [ 0.5 ] 3 0 1) work
                        |> ignore
                    )
                    "Foreign work cannot enter the session."

                Expect.isTrue rejected "The worker observes rejection."
            testCase "Runs require target work and progress"
            <| fun _ ->
                for withTarget, withProgress in
                    [
                        false, true
                        true, false
                    ] do
                    let clock = Clock()

                    let work (s: Session) =
                        s.Region(
                            (if withTarget then target else other),
                            fun () -> clock.Advance 100L
                        )

                        if withProgress then
                            s.Progress()

                    Expect.throwsT<InvalidOperationException>
                        (fun () ->
                            Experiment.runWith clock (settings [ 0.5 ] 3 0 1) work
                            |> ignore
                        )
                        "Reject incomplete evidence."
            testCase "Runs require positive elapsed time"
            <| fun _ ->
                let clock = Clock()

                Expect.throwsT<InvalidOperationException>
                    (fun () ->
                        Experiment.runWith
                            clock
                            (settings [ 0.0 ] 3 0 1)
                            (fun s ->
                                s.Region(target, ignore)
                                s.Progress()
                            )
                        |> ignore
                    )
                    "Zero duration cannot produce a ratio."
            testCase "Progress must match across runs"
            <| fun _ ->
                let clock = Clock()
                let mutable invocation = 0

                Expect.throwsT<InvalidOperationException>
                    (fun () ->
                        Experiment.runWith
                            clock
                            (settings [ 0.5 ] 3 0 1)
                            (fun s ->
                                workload clock s

                                invocation <-
                                    invocation
                                    + 1

                                if invocation = 2 then
                                    s.Progress()
                            )
                        |> ignore
                    )
                    "Different amounts of work cannot be compared."
            testCase "Progress inside a region is rejected"
            <| fun _ ->
                let clock = Clock()

                Expect.throwsT<InvalidOperationException>
                    (fun () ->
                        Experiment.runWith
                            clock
                            (settings [ 0.5 ] 3 0 1)
                            (fun s ->
                                s.Region(
                                    target,
                                    fun () ->
                                        clock.Advance 100L
                                        s.Progress()
                                )
                            )
                        |> ignore
                    )
                    "Record progress after CPU work."
            testCase "Sessions close after success and failure"
            <| fun _ ->
                for fail in
                    [
                        false
                        true
                    ] do
                    let clock = Clock()
                    let mutable captured = None
                    let failure = ApplicationException "workload failed"

                    let action () =
                        Experiment.runWith
                            clock
                            (settings [ 0.5 ] 3 0 1)
                            (fun s ->
                                captured <- Some s
                                workload clock s

                                if fail then
                                    raise failure
                            )
                        |> ignore

                    if fail then
                        let observed =
                            try
                                action ()
                                None
                            with e ->
                                Some e

                        Expect.isTrue
                            (observed
                             |> Option.exists (fun e -> Object.ReferenceEquals(e, failure)))
                            "Preserve the original failure."
                    else
                        action ()

                    Expect.throwsT<InvalidOperationException>
                        (captured
                         |> Option.get)
                            .Progress
                        "A retained session is closed."
            testCase "Caught region failures invalidate measurements"
            <| fun _ ->
                let clock = Clock()

                Expect.throwsT<InvalidOperationException>
                    (fun () ->
                        Experiment.runWith
                            clock
                            (settings [ 0.5 ] 3 0 1)
                            (fun s ->
                                s.Region(target, fun () -> clock.Advance 100L)

                                try
                                    s.Region(target, fun () -> failwith "region failed")
                                with _ ->
                                    ()

                                s.Progress()
                            )
                        |> ignore
                    )
                    "A failed region invalidates the run."
        ]
