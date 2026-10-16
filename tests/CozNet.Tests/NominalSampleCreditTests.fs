namespace CozNet.Tests

open System
open CozNet
open Expecto

module NominalSampleCreditTests =
    let private parsed result = Result.defaultWith failwith result

    let private stream frequency percent period =
        NominalSampleCreditStream.tryCreate frequency percent period
        |> parsed

    let private sample identity sequence epoch period state location =
        NominalSampleObservation.tryCreate identity sequence epoch period state location
        |> parsed

    let private selected identity sequence epoch period =
        sample identity sequence epoch period SampleThreadState.Runnable SampleLocation.Selected

    let private credit observation state =
        let amount, next =
            NominalSampleCreditStream.observe observation state
            |> parsed

        DelayTicks.value amount, next

    [<Tests>]
    let tests =
        testList "Nominal sample credit" [
            testCase "First and resumed events retain nominal weight"
            <| fun _ ->
                for percent, expected in
                    [
                        0, 0L
                        50, 500000L
                        100, 1000000L
                    ] do
                    let initial = stream 1000000000L percent 1000000UL
                    let identity = NominalSampleCreditStream.identity initial
                    let first, next = credit (selected identity 1UL 1UL 1000000UL) initial
                    let resumed, next = credit (selected identity 2UL 3UL 1000000UL) next
                    let later, _ = credit (selected identity 3UL 3UL 1000000UL) next

                    Expect.equal
                        (first, resumed, later)
                        (expected, expected, expected)
                        "Every admitted selected event has the same weight, including after empty epochs."

            testCase "Fractional credit survives exclusions and epoch changes"
            <| fun _ ->
                let initial = stream 3L 50 100000000UL
                let identity = NominalSampleCreditStream.identity initial
                let mutable state = initial
                let mutable total = 0L

                for index in 1UL .. 20UL do
                    let location =
                        if index % 2UL = 0UL then
                            SampleLocation.Unmapped
                        else
                            SampleLocation.Selected

                    let amount, next =
                        credit
                            (sample
                                identity
                                index
                                index
                                100000000UL
                                SampleThreadState.Runnable
                                location)
                            state

                    total <-
                        total
                        + amount

                    state <- next

                Expect.equal
                    total
                    1L
                    "Ten selected events retain their fractional remainder across ten unmapped events."

                for threadState in
                    [
                        SampleThreadState.Blocked
                        SampleThreadState.Delaying
                        SampleThreadState.Unavailable
                    ] do
                    let amount, afterExcluded =
                        credit
                            (sample
                                identity
                                21UL
                                21UL
                                100000000UL
                                threadState
                                SampleLocation.Selected)
                            state

                    Expect.equal amount 0L "Non-runnable events earn no credit."

                    let carried, _ =
                        [ 22UL .. 25UL ]
                        |> List.fold
                            (fun (sum, current) sequence ->
                                let amount, next =
                                    credit
                                        (selected identity sequence sequence 100000000UL)
                                        current

                                sum
                                + amount,
                                next
                            )
                            (0L, afterExcluded)

                    Expect.equal
                        carried
                        1L
                        "Non-runnable events preserve the remainder for later selected events."

                let other, afterOther =
                    credit
                        (sample
                            identity
                            21UL
                            21UL
                            100000000UL
                            SampleThreadState.Runnable
                            SampleLocation.Other)
                        state

                Expect.equal other 0L "Other locations earn no credit."

                let carried, _ =
                    [ 22UL .. 25UL ]
                    |> List.fold
                        (fun (sum, current) sequence ->
                            let amount, next =
                                credit (selected identity sequence sequence 100000000UL) current

                            sum
                            + amount,
                            next
                        )
                        (0L, afterOther)

                Expect.equal
                    carried
                    1L
                    "Other locations preserve the remainder for later selected events."

            testCase "Invalid evidence does not consume the next event"
            <| fun _ ->
                let initial = stream 1000000000L 50 1000000UL
                let identity = NominalSampleCreditStream.identity initial
                let _, state = credit (selected identity 1UL 2UL 1000000UL) initial

                for invalid in
                    [
                        selected (Guid.NewGuid()) 2UL 2UL 1000000UL
                        selected identity 1UL 2UL 1000000UL
                        selected identity 3UL 2UL 1000000UL
                        selected identity 2UL 1UL 1000000UL
                        selected identity 2UL 2UL 999999UL
                    ] do
                    Expect.isError
                        (NominalSampleCreditStream.observe invalid state)
                        "Foreign, duplicate, gapped, stale, or changed-period events reject."

                let amount, _ = credit (selected identity 2UL 2UL 1000000UL) state
                Expect.equal amount 500000L "Rejected evidence leaves the original stream usable."

            testCase "Input and credit overflow reject"
            <| fun _ ->
                for frequency, percent, period in
                    [
                        0L, 50, 1UL
                        -1L, 50, 1UL
                        1L, -1, 1UL
                        1L, 101, 1UL
                        1L, 50, 0UL
                    ] do
                    Expect.isError
                        (NominalSampleCreditStream.tryCreate frequency percent period)
                        "Invalid settings cannot create a stream."

                for sequence, epoch, period in
                    [
                        0UL, 1UL, 1UL
                        1UL, 0UL, 1UL
                        1UL, 1UL, 0UL
                    ] do
                    Expect.isError
                        (NominalSampleObservation.tryCreate
                            Guid.Empty
                            sequence
                            epoch
                            period
                            SampleThreadState.Runnable
                            SampleLocation.Selected)
                        "An observation requires positive identity counters and period."

                let huge = stream Int64.MaxValue 100 UInt64.MaxValue

                Expect.isError
                    (NominalSampleCreditStream.observe
                        (selected (NominalSampleCreditStream.identity huge) 1UL 1UL UInt64.MaxValue)
                        huge)
                    "Conversion cannot wrap a ledger tick count."

            testCase "Signal accounting separates estimators and retains pending epoch gates"
            <| fun _ ->
                let selectedThread, state =
                    SignalAccounting.registerNominal 50 1000000UL (SignalAccounting.create ())
                    |> parsed

                let peer, state =
                    SignalAccounting.register (Some 100) state
                    |> parsed

                let identity =
                    SignalAccounting.sampleIdentity selectedThread state
                    |> parsed

                let observe sequence epoch state =
                    SignalAccounting.observeNominal
                        selectedThread
                        (selected identity sequence epoch 1000000UL)
                        state

                let first, state =
                    observe 1UL 1UL state
                    |> parsed

                Expect.equal
                    (DelayTicks.value first)
                    500000L
                    "The ledger receives the initial nominal credit."

                let interval =
                    CpuSampleObservation.tryCreate
                        identity
                        2UL
                        2000000UL
                        1UL
                        SampleThreadState.Runnable
                        SampleLocation.Selected
                    |> parsed

                Expect.isError
                    (SignalAccounting.observe selectedThread interval state)
                    "Interval observations cannot enter nominal streams."

                let peerIdentity =
                    SignalAccounting.sampleIdentity peer state
                    |> parsed

                Expect.isError
                    (SignalAccounting.observeNominal
                        peer
                        (selected peerIdentity 1UL 1UL 1000000UL)
                        state)
                    "Nominal observations cannot enter interval streams."

                let peerSample =
                    CpuSampleObservation.tryCreate
                        peerIdentity
                        1UL
                        1000000UL
                        0UL
                        SampleThreadState.Runnable
                        SampleLocation.Selected
                    |> parsed

                let _, state =
                    SignalAccounting.observe peer peerSample state
                    |> parsed

                let _, state =
                    SignalAccounting.prepare peer state
                    |> parsed

                let request, pending =
                    SignalAccounting.prepare selectedThread state
                    |> parsed

                let request = Option.get request
                Expect.isError (observe 2UL 2UL pending) "Pending payment excludes nominal credit."

                let released =
                    SignalAccounting.rejectedBeforeSend request pending
                    |> parsed

                Expect.isError
                    (observe 2UL 1UL released)
                    "Old epoch events remain excluded after rejected submission."

                let resumed, _ =
                    observe 2UL 2UL released
                    |> parsed

                Expect.equal
                    (DelayTicks.value resumed)
                    500000L
                    "The first resumed event credits once without an interval baseline."
        ]
