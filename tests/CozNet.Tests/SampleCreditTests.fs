namespace CozNet.Tests

open System
open System.Threading
open CozNet
open Expecto

module SampleCreditTests =
    let private parsed result = Result.defaultWith failwith result

    let private stream frequency percent =
        SampleCreditStream.tryCreate frequency percent 0UL 0UL SampleThreadState.Runnable
        |> parsed

    let private observation source sequence cpu epoch state location =
        CpuSampleObservation.tryCreate source sequence cpu epoch state location
        |> parsed

    let private sample source sequence cpu =
        observation source sequence cpu 0UL SampleThreadState.Runnable SampleLocation.Selected

    let private credit sample stream =
        let amount, next =
            SampleCreditStream.observe sample stream
            |> parsed

        DelayTicks.value amount, next

    [<Tests>]
    let tests =
        testList "Sample credit conversion" [
            testCase "Selected CPU duration uses Coz speedup rather than serial slowdown"
            <| fun _ ->
                let state = stream 1000L 50

                let amount, _ =
                    credit (sample (SampleCreditStream.identity state) 1UL 100000000UL) state

                Expect.equal amount 50L "A 100 ms sample at 50 percent earns 50 ms, not 100 ms."

                for percent, expected in
                    [
                        0, 0L
                        100, 100L
                    ] do
                    let state = stream 1000L percent

                    let amount, _ =
                        credit (sample (SampleCreditStream.identity state) 1UL 100000000UL) state

                    Expect.equal amount expected "Endpoint speedups must preserve exact credit."

            testCase "Fractional credit survives packet boundaries"
            <| fun _ ->
                let mutable state = stream 3L 50
                let identity = SampleCreditStream.identity state
                let mutable total = 0L

                for sequence in 1UL .. 10UL do
                    let amount, next =
                        credit
                            (sample
                                identity
                                sequence
                                (sequence
                                 * 100000000UL))
                            state

                    total <-
                        total
                        + amount

                    state <- next

                Expect.equal total 1L "One second earns one whole tick and retains half a tick."
                let amount, _ = credit (sample identity 11UL 2000000000UL) state

                Expect.equal
                    (total
                     + amount)
                    3L
                    "Two seconds must earn three ticks regardless of packet boundaries."

            testCase "State changes and gaps cannot credit ambiguous CPU intervals"
            <| fun _ ->
                let mutable state = stream 1000000000L 25
                let identity = SampleCreditStream.identity state

                let cases = [
                    1UL, 100UL, 0UL, SampleThreadState.Runnable, SampleLocation.Selected, 25L
                    2UL, 200UL, 1UL, SampleThreadState.Blocked, SampleLocation.Selected, 0L
                    3UL, 300UL, 2UL, SampleThreadState.Runnable, SampleLocation.Selected, 0L
                    4UL, 400UL, 2UL, SampleThreadState.Runnable, SampleLocation.Selected, 25L
                    6UL, 600UL, 2UL, SampleThreadState.Runnable, SampleLocation.Selected, 0L
                    7UL, 700UL, 2UL, SampleThreadState.Runnable, SampleLocation.Other, 0L
                    8UL, 800UL, 3UL, SampleThreadState.Delaying, SampleLocation.Selected, 0L
                    9UL, 900UL, 4UL, SampleThreadState.Runnable, SampleLocation.Unmapped, 0L
                    10UL, 1000UL, 4UL, SampleThreadState.Runnable, SampleLocation.Selected, 25L
                ]

                for sequence, cpu, epoch, status, location, expected in cases do
                    let amount, next =
                        credit (observation identity sequence cpu epoch status location) state

                    Expect.equal amount expected $"Incorrect credit at sequence {sequence}."
                    state <- next

            testCase "Foreign duplicate and regressing observations fail without state changes"
            <| fun _ ->
                let initial = stream 1000L 50
                let identity = SampleCreditStream.identity initial

                let first =
                    observation
                        identity
                        1UL
                        100000000UL
                        1UL
                        SampleThreadState.Runnable
                        SampleLocation.Selected

                let _, state = credit first initial

                for invalid in
                    [
                        first
                        sample (Guid.NewGuid()) 2UL 200000000UL
                        observation
                            identity
                            2UL
                            99999999UL
                            1UL
                            SampleThreadState.Runnable
                            SampleLocation.Selected
                        sample identity 2UL 200000000UL
                    ] do
                    Expect.isError
                        (SampleCreditStream.observe invalid state)
                        "Rejected observations must not advance the stream."

                let amount, _ =
                    credit
                        (observation
                            identity
                            2UL
                            200000000UL
                            1UL
                            SampleThreadState.Runnable
                            SampleLocation.Selected)
                        state

                Expect.equal
                    amount
                    50L
                    "A valid observation must remain acceptable after rejected inputs."

            testCase "Bounds and overflow reject invalid credit"
            <| fun _ ->
                for frequency, percent in
                    [
                        0L, 50
                        -1L, 50
                        1L, -1
                        1L, 101
                    ] do
                    Expect.isError
                        (SampleCreditStream.tryCreate
                            frequency
                            percent
                            0UL
                            0UL
                            SampleThreadState.Runnable)
                        "Invalid conversion settings must fail."

                Expect.isError
                    (CpuSampleObservation.tryCreate
                        Guid.Empty
                        0UL
                        0UL
                        0UL
                        SampleThreadState.Runnable
                        SampleLocation.Selected)
                    "Sequence zero belongs to the baseline."

                let state = stream Int64.MaxValue 100
                let identity = SampleCreditStream.identity state

                Expect.isError
                    (SampleCreditStream.observe (sample identity 1UL UInt64.MaxValue) state)
                    "Credit must not wrap or saturate."

                let amount, _ = credit (sample identity 1UL 0UL) state
                Expect.equal amount 0L "Overflow must leave the original stream usable."

            testCase "A rejected ledger transition does not consume the observation"
            <| fun _ ->
                let runtime =
                    CooperativeRuntime {
                        Frequency = 1000L
                        Timestamp = fun () -> 0L
                        Wait = fun _ _ -> failwith "This case must not wait."
                    }

                let owner =
                    runtime.Register None
                    |> parsed

                let identity =
                    runtime.AttachSamples(owner, 50, 0UL, 0UL)
                    |> parsed

                runtime.BeginBlock owner
                |> parsed

                let blocked =
                    observation
                        identity
                        1UL
                        100000000UL
                        1UL
                        SampleThreadState.Blocked
                        SampleLocation.Unmapped

                Expect.isError
                    (runtime.CreditObservation(owner, blocked))
                    "A blocked ledger entry must reject credit processing."

                runtime.EndBlock(owner, WakeReason.TimeoutOrSpurious)
                |> parsed

                let amount =
                    runtime.CreditObservation(owner, blocked)
                    |> parsed
                    |> DelayTicks.value

                Expect.equal
                    amount
                    0L
                    "Retry must preserve the excluded blocked interval without reporting a duplicate."

            testCase "Runtime owns stream replay protection and credits the ledger atomically"
            <| fun _ ->
                let runtime =
                    CooperativeRuntime {
                        Frequency = 1000L
                        Timestamp = fun () -> 0L
                        Wait =
                            fun _ _ ->
                                failwith "The selected thread must not wait for its own credit."
                    }

                let owner =
                    runtime.Register None
                    |> parsed

                let identity =
                    runtime.AttachSamples(owner, 50, 0UL, 0UL)
                    |> parsed

                Expect.isError
                    (runtime.AttachSamples(owner, 50, 0UL, 0UL))
                    "Replacing a stream would permit replay."

                let first = sample identity 1UL 100000000UL

                let amount =
                    runtime.CreditObservation(owner, first)
                    |> parsed
                    |> DelayTicks.value

                Expect.equal amount 50L "The runtime must apply the converted credit."

                runtime.Check(owner, CancellationToken.None)
                |> parsed

                Expect.equal
                    (DelayTicks.value runtime.GlobalDelay)
                    50L
                    "The owning thread publishes sample credit."

                Expect.isError
                    (runtime.CreditObservation(owner, first))
                    "A duplicate packet cannot credit the runtime twice."

                runtime.Unregister owner
                |> parsed

                Expect.isError
                    (runtime.CreditObservation(owner, sample identity 2UL 200000000UL))
                    "Exit must remove the credit stream."
        ]
