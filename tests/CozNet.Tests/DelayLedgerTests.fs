namespace CozNet.Tests

open System
open CozNet
open Expecto

module DelayLedgerTests =
    let private parsed result = Result.defaultWith failwith result

    let private ticks value =
        DelayTicks.tryCreate value
        |> parsed

    let private register parent ledger =
        DelayLedger.register parent ledger
        |> parsed

    let private credit thread amount ledger =
        DelayLedger.creditSample thread (ticks amount) ledger
        |> parsed

    let private catchUp thread ledger =
        DelayLedger.catchUp thread ledger
        |> parsed

    let private completed thread amount ledger =
        DelayLedger.completeDelay thread (ticks amount) ledger
        |> parsed

    let private blocked thread ledger =
        DelayLedger.beginBlock thread ledger
        |> parsed

    let private resumed thread reason ledger =
        DelayLedger.endBlock thread reason ledger
        |> parsed

    let private globalTicks ledger =
        DelayLedger.globalDelay ledger
        |> DelayTicks.value

    let private pair () =
        let selected, ledger =
            DelayLedger.create ()
            |> register None

        let worker, ledger =
            ledger
            |> register None

        selected, worker, ledger

    let private publish thread amount ledger =
        let action, next =
            ledger
            |> credit thread amount
            |> catchUp thread

        Expect.equal
            action
            DelayAction.Continue
            "The credited thread must not pay its own virtual delay."

        next

    [<Tests>]
    let tests =
        testList "Sampled delay ledger" [
            testCase "Peer snapshot aggregate retains credit that arrives during child service"
            <| fun _ ->
                let selected, worker, ledger = pair ()

                let _, reserved =
                    ledger
                    |> publish selected 110000L
                    |> catchUp worker

                let aggregateAction, aggregate =
                    reserved
                    |> publish selected 40000L
                    |> completed worker 115000L
                    |> catchUp worker

                let _, first =
                    reserved
                    |> completed worker 50000L
                    |> catchUp worker

                let _, second =
                    first
                    |> publish selected 40000L
                    |> completed worker 50000L
                    |> catchUp worker

                let childAction, children =
                    second
                    |> completed worker 15000L
                    |> catchUp worker

                Expect.equal
                    aggregateAction
                    (DelayAction.Wait(ticks 35000L))
                    "New credit must remain unpaid after the original snapshot is serviced."

                Expect.equal
                    childAction
                    aggregateAction
                    "A terminal aggregate must preserve the child journal's remaining debt."

                Expect.equal
                    (globalTicks aggregate)
                    150000L
                    "Peer service must not publish the selected credit twice."

                Expect.equal
                    (globalTicks children)
                    150000L
                    "Child service must preserve the same published credit."

            testCase "Peer snapshot terminal overshoot publishes once after aggregate settlement"
            <| fun _ ->
                let selected, worker, ledger = pair ()

                let _, reserved =
                    ledger
                    |> publish selected 100000L
                    |> catchUp worker

                let aggregateAction, aggregate =
                    reserved
                    |> publish selected 40000L
                    |> completed worker 200000L
                    |> catchUp worker

                let _, first =
                    reserved
                    |> completed worker 50000L
                    |> catchUp worker

                let childAction, children =
                    first
                    |> publish selected 40000L
                    |> completed worker 150000L
                    |> catchUp worker

                let selectedAction, _ = catchUp selected aggregate
                let _, unchanged = catchUp worker aggregate

                Expect.equal
                    aggregateAction
                    DelayAction.Continue
                    "The peer has serviced its snapshot and retains measured overshoot."

                Expect.equal
                    childAction
                    aggregateAction
                    "Separate child settlements must reach the same terminal action."

                Expect.equal
                    (globalTicks aggregate)
                    200000L
                    "Measured overshoot must not be capped to the authorized snapshot."

                Expect.equal
                    (globalTicks children)
                    200000L
                    "The child journal must produce the same global delay."

                Expect.equal
                    (globalTicks unchanged)
                    200000L
                    "Another check must not publish the aggregate twice."

                Expect.equal
                    selectedAction
                    (DelayAction.Wait(ticks 60000L))
                    "The selected thread owes the peer's actual lead."

            testCase "Delay ticks reject negative values and preserve endpoints"
            <| fun _ ->
                Expect.isError
                    (DelayTicks.tryCreate -1L)
                    "Negative durations must not enter the ledger."

                for value in
                    [
                        0L
                        1L
                        Int64.MaxValue
                    ] do
                    Expect.equal
                        (ticks value
                         |> DelayTicks.value)
                        value
                        "Parsing must preserve exact ticks."

            testCase "Selected samples exempt their thread and delay another worker"
            <| fun _ ->
                let selected, worker, ledger = pair ()
                let ledger = publish selected 10L ledger
                let action, ledger = catchUp worker ledger

                Expect.equal
                    action
                    (DelayAction.Wait(ticks 10L))
                    "The other worker owes the selected sample credit."

                let action, ledger =
                    ledger
                    |> completed worker 10L
                    |> catchUp worker

                Expect.equal action DelayAction.Continue "A paid delay must not be charged twice."

                Expect.equal
                    (globalTicks ledger)
                    10L
                    "One worker payment must not add global delay."

            testCase "Concurrent selected credits represent overlapping delay rather than their sum"
            <| fun _ ->
                let first, second, ledger = pair ()
                let observer, ledger = register None ledger

                let ledger =
                    ledger
                    |> credit first 10L
                    |> credit second 10L

                let _, ledger = catchUp first ledger
                let _, ledger = catchUp second ledger
                let action, _ = catchUp observer ledger

                Expect.equal
                    action
                    (DelayAction.Wait(ticks 10L))
                    "Overlapping selected work must not produce 20 ticks of debt."

            testCase "A dependency wake skips only delay accrued while blocked"
            <| fun _ ->
                let selected, worker, ledger = pair ()

                let ledger =
                    ledger
                    |> publish selected 4L
                    |> blocked worker
                    |> publish selected 6L

                let action, _ =
                    ledger
                    |> resumed worker WakeReason.Dependency
                    |> catchUp worker

                Expect.equal
                    action
                    (DelayAction.Wait(ticks 4L))
                    "The pre-block debt remains after skipping the blocked interval."

            testCase "A timeout wake retains delay accrued while blocked"
            <| fun _ ->
                let selected, worker, ledger = pair ()

                let ledger =
                    ledger
                    |> publish selected 4L
                    |> blocked worker
                    |> publish selected 6L

                let action, _ =
                    ledger
                    |> resumed worker WakeReason.TimeoutOrSpurious
                    |> catchUp worker

                Expect.equal
                    action
                    (DelayAction.Wait(ticks 10L))
                    "A timeout cannot inherit delay from a dependency."

            testCase "A settled waiter does not pay its dependency delay twice"
            <| fun _ ->
                let selected, worker, ledger = pair ()

                let ledger =
                    ledger
                    |> blocked worker
                    |> publish selected 10L

                let action, _ =
                    ledger
                    |> resumed worker WakeReason.Dependency
                    |> catchUp worker

                Expect.equal
                    action
                    DelayAction.Continue
                    "The producer already incurred this virtual delay before the wake."

            testCase "Blocked threads cannot sample wait block again or exit"
            <| fun _ ->
                let _, worker, ledger = pair ()
                let ledger = blocked worker ledger

                Expect.isError
                    (DelayLedger.creditSample worker (ticks 1L) ledger)
                    "Blocked time cannot count as selected CPU work."

                Expect.isError
                    (DelayLedger.catchUp worker ledger)
                    "A blocked thread cannot execute a delay."

                Expect.isError
                    (DelayLedger.beginBlock worker ledger)
                    "Nested block intervals would lose the original credit boundary."

                Expect.isError
                    (DelayLedger.unregister worker ledger)
                    "A blocked thread must resume before exit."

            testCase "A partial wait retains debt and excludes samples during the wait"
            <| fun _ ->
                let selected, worker, ledger = pair ()

                let _, ledger =
                    ledger
                    |> publish selected 10L
                    |> catchUp worker

                Expect.isError
                    (DelayLedger.creditSample worker (ticks 1L) ledger)
                    "Profiler delay cannot count as sampled CPU work."

                Expect.isError
                    (DelayLedger.catchUp worker ledger)
                    "An outstanding wait must complete first."

                Expect.isError
                    (DelayLedger.beginBlock worker ledger)
                    "A wait cannot change into a dependency block."

                Expect.isError
                    (DelayLedger.unregister worker ledger)
                    "An outstanding wait cannot disappear at thread exit."

                let action, _ =
                    ledger
                    |> completed worker 3L
                    |> catchUp worker

                Expect.equal
                    action
                    (DelayAction.Wait(ticks 7L))
                    "An interrupted wait pays only its actual duration."

            testCase "Global delay added during a wait remains payable"
            <| fun _ ->
                let selected, worker, ledger = pair ()

                let _, ledger =
                    ledger
                    |> publish selected 10L
                    |> catchUp worker

                let ledger = publish selected 5L ledger

                let action, _ =
                    ledger
                    |> completed worker 10L
                    |> catchUp worker

                Expect.equal
                    action
                    (DelayAction.Wait(ticks 5L))
                    "Completion must not erase new global debt."

            testCase "Linux wait overshoot propagates to the global ledger"
            <| fun _ ->
                let selected, worker, ledger = pair ()

                let _, ledger =
                    ledger
                    |> publish selected 10L
                    |> catchUp worker

                let action, ledger =
                    ledger
                    |> completed worker 12L
                    |> catchUp worker

                Expect.equal action DelayAction.Continue "The worker paid more than requested."

                Expect.equal
                    (globalTicks ledger)
                    12L
                    "Linux accounting includes measured overshoot."

                let action, _ = catchUp selected ledger

                Expect.equal
                    action
                    (DelayAction.Wait(ticks 2L))
                    "The other thread must account for the propagated overshoot."

            testCase "Children inherit parent debt while independent threads start at global delay"
            <| fun _ ->
                let selected, parent, ledger = pair ()
                let ledger = publish selected 10L ledger
                let child, ledger = register (Some parent) ledger
                let independent, ledger = register None ledger
                let action, ledger = catchUp child ledger

                Expect.equal
                    action
                    (DelayAction.Wait(ticks 10L))
                    "A child must not erase inherited debt."

                let action, _ = catchUp independent ledger

                Expect.equal
                    action
                    DelayAction.Continue
                    "An independent registration starts in the current delay epoch."

            testCase "Thread exit requires both owed delay and unpublished credit to settle"
            <| fun _ ->
                let selected, worker, ledger = pair ()
                let ledger = credit selected 10L ledger

                Expect.isError
                    (DelayLedger.unregister selected ledger)
                    "An exit cannot discard unpublished selected credit."

                let _, ledger = catchUp selected ledger
                Expect.isError (DelayLedger.unregister worker ledger) "An exit cannot discard debt."
                let _, ledger = catchUp worker ledger

                let ledger =
                    ledger
                    |> completed worker 10L
                    |> DelayLedger.unregister worker
                    |> parsed

                Expect.isError (DelayLedger.catchUp worker ledger) "Exited handles must fail."
                let replacement, ledger = register None ledger

                Expect.notEqual
                    replacement
                    worker
                    "A stale handle must not identify a later registration."

                Expect.isError
                    (DelayLedger.creditSample worker (ticks 1L) ledger)
                    "A stale sample must not reach a replacement thread."

            testCase "Foreign handles and unmatched completions fail"
            <| fun _ ->
                let _, worker, ledger = pair ()
                let _, foreign, _ = pair ()

                Expect.isError
                    (DelayLedger.catchUp foreign ledger)
                    "Thread handles belong to one ledger."

                Expect.isError
                    (DelayLedger.register (Some foreign) ledger)
                    "Parent handles belong to one ledger."

                Expect.isError
                    (DelayLedger.completeDelay worker (ticks 1L) ledger)
                    "Completion needs a pending wait."

                Expect.isError
                    (DelayLedger.endBlock worker WakeReason.Dependency ledger)
                    "A wake needs a blocked interval."

                let blockedLedger = blocked worker ledger

                Expect.isError
                    (DelayLedger.register (Some worker) blockedLedger)
                    "A blocked parent cannot create a child."

            testCase
                "Effective duration subtracts global delay once and rejects nonpositive results"
            <| fun _ ->
                let selected, worker, ledger = pair ()

                let _, ledger =
                    ledger
                    |> publish selected 10L
                    |> catchUp worker

                let ledger = completed worker 10L ledger

                let effective =
                    DelayLedger.effectiveDuration (ticks 100L) ledger
                    |> parsed
                    |> DelayTicks.value

                Expect.equal
                    effective
                    90L
                    "Subtract global virtual delay, not the sum of per-thread credits."

                for elapsed in
                    [
                        0L
                        9L
                        10L
                    ] do
                    Expect.isError
                        (DelayLedger.effectiveDuration (ticks elapsed) ledger)
                        "Nonpositive corrected durations must not enter results."

            testCase "Overflow rejects a transition without corrupting the prior ledger"
            <| fun _ ->
                let selected, worker, ledger = pair ()
                let ledger = publish selected Int64.MaxValue ledger

                Expect.isError
                    (DelayLedger.creditSample selected (ticks 1L) ledger)
                    "Selected credit cannot wrap."

                Expect.equal
                    (globalTicks ledger)
                    Int64.MaxValue
                    "Rejected transitions leave the input intact."

                let action, _ = catchUp worker ledger

                Expect.equal
                    action
                    (DelayAction.Wait(ticks Int64.MaxValue))
                    "The outstanding debt remains exact."

            testCase "Zero credits and zero completed waits do not invent progress"
            <| fun _ ->
                let selected, worker, ledger = pair ()
                let ledger = publish selected 0L ledger
                Expect.equal (globalTicks ledger) 0L "A null intervention changes no global delay."

                let _, ledger =
                    ledger
                    |> publish selected 10L
                    |> catchUp worker

                let action, _ =
                    ledger
                    |> completed worker 0L
                    |> catchUp worker

                Expect.equal
                    action
                    (DelayAction.Wait(ticks 10L))
                    "A zero-duration wait pays no debt."
        ]
