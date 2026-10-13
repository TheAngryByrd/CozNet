namespace CozNet.Tests

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open CozNet
open Expecto

module CooperativeRuntimeTests =
    let private parsed result = Result.defaultWith failwith result

    let private ticks amount =
        DelayTicks.tryCreate amount
        |> parsed

    let private withWorker (runtime: CooperativeRuntime) publish action =
        use ready = new ManualResetEventSlim(false)
        use proceed = new ManualResetEventSlim(false)

        let result =
            TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)

        let worker =
            Thread(
                ThreadStart(fun () ->
                    try
                        let handle =
                            runtime.Register None
                            |> parsed

                        ready.Set()

                        if not (proceed.Wait(TimeSpan.FromSeconds 5.)) then
                            failwith "The worker did not receive its start signal."

                        action handle
                        result.SetResult()
                    with error ->
                        result.SetException error
                )
            )

        worker.IsBackground <- true
        worker.Start()
        let mutable publicationError = None

        try
            Expect.isTrue
                (ready.Wait(TimeSpan.FromSeconds 5.))
                "The worker must register before publishing delay."

            publish ()
        with error ->
            publicationError <- Some error

        proceed.Set()

        Expect.isTrue
            (worker.Join(TimeSpan.FromSeconds 5.))
            "The worker must finish within five seconds."

        publicationError
        |> Option.iter raise

        result.Task.GetAwaiter().GetResult()

    [<Tests>]
    let tests =
        testList "Cooperative delay runtime" [
            testCase "A live thread pays debt and publishes measured overshoot"
            <| fun _ ->
                let runtime = CooperativeRuntime(DelayRuntimeTiming.real)

                let selected =
                    runtime.Register None
                    |> parsed

                let credit =
                    Stopwatch.Frequency
                    / 100L

                let mutable measured = 0L

                withWorker
                    runtime
                    (fun () ->
                        runtime.CreditSample(selected, ticks credit)
                        |> parsed

                        runtime.Check(selected, CancellationToken.None)
                        |> parsed
                    )
                    (fun worker ->
                        let start = Stopwatch.GetTimestamp()

                        runtime.Check(worker, CancellationToken.None)
                        |> parsed

                        measured <-
                            Stopwatch.GetTimestamp()
                            - start

                        runtime.Unregister worker
                        |> parsed
                    )

                Expect.isGreaterThanOrEqual
                    measured
                    credit
                    "The worker must incur the requested ten-millisecond delay."

                Expect.isGreaterThanOrEqual
                    (DelayTicks.value runtime.GlobalDelay)
                    credit
                    "Actual wait time must settle the debt."

            testCase "A cancelled partial wait retains debt for a later check"
            <| fun _ ->
                let mutable now = 0L
                let waits = ResizeArray<int64>()
                use cancellation = new CancellationTokenSource()

                let runtime =
                    CooperativeRuntime {
                        Frequency = 1000L
                        Timestamp = fun () -> now
                        Wait =
                            fun duration token ->
                                waits.Add(DelayTicks.value duration)

                                if waits.Count = 1 then
                                    now <- now + 3L
                                    cancellation.Cancel()
                                    token.ThrowIfCancellationRequested()
                                else
                                    now <-
                                        now
                                        + DelayTicks.value duration
                    }

                let selected =
                    runtime.Register None
                    |> parsed

                withWorker
                    runtime
                    (fun () ->
                        runtime.CreditSample(selected, ticks 10L)
                        |> parsed

                        runtime.Check(selected, CancellationToken.None)
                        |> parsed
                    )
                    (fun worker ->
                        Expect.isError
                            (runtime.Check(worker, cancellation.Token))
                            "Cancellation must be reported."

                        Expect.isError
                            (runtime.Unregister worker)
                            "A partial wait must not discard debt."

                        runtime.Check(worker, CancellationToken.None)
                        |> parsed

                        runtime.Unregister worker
                        |> parsed
                    )

                Expect.sequenceEqual
                    waits
                    [
                        10L
                        7L
                    ]
                    "Only the measured three ticks can reduce the remaining debt."

            testCase "Sample updates proceed while another thread waits"
            <| fun _ ->
                let mutable now = 0L
                let mutable duringWait = ignore

                let runtime =
                    CooperativeRuntime {
                        Frequency = 1000L
                        Timestamp = fun () -> now
                        Wait =
                            fun duration _ ->
                                duringWait ()

                                now <-
                                    now
                                    + DelayTicks.value duration
                    }

                let selected =
                    runtime.Register None
                    |> parsed

                withWorker
                    runtime
                    (fun () ->
                        runtime.CreditSample(selected, ticks 10L)
                        |> parsed

                        runtime.Check(selected, CancellationToken.None)
                        |> parsed
                    )
                    (fun worker ->
                        duringWait <-
                            fun () ->
                                let update =
                                    Task.Run(fun () -> runtime.CreditSample(selected, ticks 5L))

                                Expect.isTrue
                                    (update.Wait(TimeSpan.FromSeconds 2.))
                                    "The ledger lock must not cover the wait."

                                update.GetAwaiter().GetResult()
                                |> parsed

                                Expect.isError
                                    (runtime.CreditSample(worker, ticks 1L))
                                    "Profiler delay must not receive selected CPU credit."

                        runtime.Check(worker, CancellationToken.None)
                        |> parsed

                        runtime.Unregister worker
                        |> parsed
                    )

                runtime.Check(selected, CancellationToken.None)
                |> parsed

                Expect.equal
                    (DelayTicks.value runtime.GlobalDelay)
                    15L
                    "The concurrent sample must remain available after wait completion."

            testCase "Thread ownership and duplicate registration reject invalid calls"
            <| fun _ ->
                let runtime = CooperativeRuntime(DelayRuntimeTiming.real)

                let owner =
                    runtime.Register None
                    |> parsed

                Expect.isError
                    (runtime.Register None)
                    "One runtime thread cannot have two ledger registrations."

                withWorker
                    runtime
                    ignore
                    (fun worker ->
                        Expect.isError
                            (runtime.Check(owner, CancellationToken.None))
                            "A foreign thread cannot perform the owner's delay."

                        Expect.isError
                            (runtime.BeginBlock owner)
                            "A foreign thread cannot change the owner's state."

                        runtime.Unregister worker
                        |> parsed
                    )

                runtime.Unregister owner
                |> parsed

                let replacement =
                    runtime.Register None
                    |> parsed

                Expect.isError
                    (runtime.CreditSample(owner, ticks 1L))
                    "An expired registration must not receive samples."

                runtime.Unregister replacement
                |> parsed
        ]
