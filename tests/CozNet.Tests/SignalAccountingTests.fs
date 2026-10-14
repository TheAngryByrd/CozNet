namespace CozNet.Tests

open System
open CozNet
open Expecto

module SignalAccountingTests =
    let private parsed result = Result.defaultWith failwith result

    let private ticks value =
        DelayTicks.tryCreate value
        |> parsed

    let private prepare thread state =
        SignalAccounting.prepare thread state
        |> parsed

    let private required thread state =
        let request, next = prepare thread state
        Expect.isSome request "Unpaid delay requires a request."
        Option.get request, next

    let private observe thread sequence cpu epoch state =
        let identity =
            SignalAccounting.sampleIdentity thread state
            |> parsed

        CpuSampleObservation.tryCreate
            identity
            sequence
            cpu
            epoch
            SampleThreadState.Runnable
            SampleLocation.Selected
        |> parsed
        |> fun sample -> SignalAccounting.observe thread sample state

    let private start percent =
        let selected, state =
            SignalAccounting.register (Some percent) (SignalAccounting.create ())
            |> parsed

        let peer, state =
            SignalAccounting.register None state
            |> parsed

        selected, peer, state

    let private credited cpu =
        let selected, peer, state = start 50

        let _, state =
            observe selected 1UL cpu 0UL state
            |> parsed

        let request, state = prepare selected state
        Expect.isNone request "Selected credit publishes without a wait."
        selected, peer, state

    let private sent request sequence state =
        SignalAccounting.sent request sequence state
        |> parsed

    let private complete request sequence amount state =
        SignalAccounting.complete request sequence (SignalSettlement.Measured(ticks amount)) state
        |> parsed

    [<Tests>]
    let tests =
        testList "Signal accounting" [
            testCase "Sample credit becomes global only when published"
            <| fun _ ->
                let selected, peer, state = start 50

                let credit, state =
                    observe selected 1UL 80000UL 0UL state
                    |> parsed

                Expect.equal (DelayTicks.value credit) 40000L "Credit uses the nominal CPU period."

                Expect.equal
                    (SignalAccounting.globalNanoseconds state)
                    0L
                    "Local credit is not global credit."

                let before, state = prepare peer state
                Expect.isNone before "Unpublished credit cannot delay a peer."
                let _, state = prepare selected state
                let request, _ = required peer state

                Expect.equal
                    (SignalDelayRequest.nanoseconds request)
                    40000L
                    "Publication makes the peer owe the credit."

            testCase "Partial and deferred waits preserve bounded debt"
            <| fun _ ->
                let _, peer, state = credited 200000UL
                let first, state = required peer state

                Expect.equal
                    (SignalDelayRequest.nanoseconds first)
                    50000L
                    "A request must not exceed 50 microseconds."

                let state =
                    sent first 1UL state
                    |> complete first 1UL 30000L

                let second, state = required peer state

                Expect.equal
                    (SignalDelayRequest.nanoseconds second)
                    50000L
                    "Partial wait leaves 70 microseconds of debt."

                let state = sent second 2UL state

                let state =
                    SignalAccounting.complete second 2UL SignalSettlement.Deferred state
                    |> parsed

                let third, state = required peer state
                Expect.equal (SignalDelayRequest.nanoseconds third) 50000L "Deferral pays no debt."

                let state =
                    sent third 3UL state
                    |> complete third 3UL 40000L

                let fourth, state = required peer state

                Expect.equal
                    (SignalDelayRequest.nanoseconds fourth)
                    30000L
                    "Only actual waits reduce debt."

                let state =
                    sent fourth 4UL state
                    |> complete fourth 4UL 30000L

                let finished, state = prepare peer state
                Expect.isNone finished "The full measured wait settles the peer."

                Expect.equal
                    (SignalAccounting.pendingCount state)
                    0
                    "Every acknowledgment consumes its request."

                Expect.isOk (SignalAccounting.unregister peer state) "Settled peers can retire."

            testCase "Overshoot creates debt on the selected thread"
            <| fun _ ->
                let selected, peer, state = credited 80000UL
                let request, state = required peer state

                let state =
                    sent request 1UL state
                    |> complete request 1UL 70000L

                Expect.equal
                    (SignalAccounting.globalNanoseconds state)
                    40000L
                    "Overshoot is local until the next check."

                let extra, state = prepare peer state
                Expect.isNone extra "Overshoot publishes without another wait."

                Expect.equal
                    (SignalAccounting.globalNanoseconds state)
                    70000L
                    "Linux accounting publishes actual overshoot."

                let selectedRequest, _ = required selected state

                Expect.equal
                    (SignalDelayRequest.nanoseconds selectedRequest)
                    30000L
                    "The selected thread also pays debt."

            testCase "Live publication proceeds while another request is pending"
            <| fun _ ->
                let selected, peer, state = credited 80000UL
                let request, state = required peer state
                let state = sent request 1UL state

                Expect.isError
                    (SignalAccounting.prepare peer state)
                    "Only one request can be pending per thread."

                let _, state =
                    observe selected 2UL 100000UL 0UL state
                    |> parsed

                let _, state = prepare selected state

                Expect.equal
                    (SignalAccounting.globalNanoseconds state)
                    50000L
                    "Other runnable threads continue publishing."

                let state = complete request 1UL 20000L state
                let remainder, _ = required peer state

                Expect.equal
                    (SignalDelayRequest.nanoseconds remainder)
                    30000L
                    "Completion includes debt published while waiting."

            testCase "Acknowledgments require a sent matching request"
            <| fun _ ->
                let _, peer, state = credited 80000UL
                let request, reserved = required peer state

                Expect.isError
                    (SignalAccounting.complete request 1UL SignalSettlement.Deferred reserved)
                    "Unsent requests have no acknowledgment."

                Expect.isError
                    (SignalAccounting.sent request 0UL reserved)
                    "Native sequence zero is invalid."

                let pending = sent request 1UL reserved

                Expect.isError
                    (SignalAccounting.sent request 2UL pending)
                    "A request cannot be rebound."

                Expect.isError
                    (SignalAccounting.complete request 2UL SignalSettlement.Deferred pending)
                    "An unrelated sequence cannot settle debt."

                Expect.isError
                    (SignalAccounting.rejectedBeforeSend request pending)
                    "A sent request cannot be abandoned."

                let state = complete request 1UL 10000L pending

                Expect.isError
                    (SignalAccounting.complete request 1UL SignalSettlement.Deferred state)
                    "Duplicate acknowledgment cannot settle twice."

                let next, state = required peer state

                Expect.equal
                    (SignalDelayRequest.nanoseconds next)
                    30000L
                    "Rejected acknowledgments do not reduce debt."

                Expect.isError
                    (SignalAccounting.sent next 1UL state)
                    "A later request cannot reuse an acknowledged native sequence."

                Expect.isError
                    (SignalAccounting.complete
                        request
                        2UL
                        SignalSettlement.Deferred
                        (sent next 2UL state))
                    "A stale token cannot complete a newer request."

            testCase "Rejected enqueue pays zero and invalidates its token"
            <| fun _ ->
                let _, peer, state = credited 80000UL
                let request, state = required peer state

                let state =
                    SignalAccounting.rejectedBeforeSend request state
                    |> parsed

                let next, state = required peer state

                Expect.equal
                    (SignalDelayRequest.nanoseconds next)
                    40000L
                    "A rejected enqueue performs no wait."

                Expect.isError
                    (SignalAccounting.sent request 1UL state)
                    "An abandoned token cannot enqueue a replacement."

                Expect.isOk
                    (SignalAccounting.sent next 1UL state)
                    "A failed enqueue consumes no native sequence."

            testCase "Selected waits exclude stale epochs without consuming samples"
            <| fun _ ->
                let selected, peer, state = credited 80000UL
                let request, state = required peer state

                let state =
                    sent request 1UL state
                    |> complete request 1UL 70000L

                let _, state = prepare peer state
                let request, state = required selected state

                Expect.equal
                    (SignalAccounting.requiredEpoch selected state
                     |> parsed)
                    1UL
                    "The reservation advances the eligibility boundary."

                Expect.isError
                    (observe selected 2UL 100000UL 1UL state)
                    "Pending intervention excludes sample credit."

                let state =
                    sent request 1UL state
                    |> complete request 1UL 30000L

                Expect.isError
                    (observe selected 2UL 100000UL 0UL state)
                    "Queued observations from before intervention stay excluded."

                let first, state =
                    observe selected 2UL 100000UL 1UL state
                    |> parsed

                Expect.equal (DelayTicks.value first) 0L "The new epoch establishes a baseline."

                let second, state =
                    observe selected 3UL 120000UL 1UL state
                    |> parsed

                Expect.equal
                    (DelayTicks.value second)
                    10000L
                    "Later eligible periods resume credit."

                let _, state = prepare selected state

                Expect.equal
                    (SignalAccounting.globalNanoseconds state)
                    80000L
                    "No intervention interval earns credit."

            testCase "Zero speedup and fractional periods preserve exact credit"
            <| fun _ ->
                let selected, peer, state = start 0

                let credit, state =
                    observe selected 1UL 1000000UL 0UL state
                    |> parsed

                Expect.equal (DelayTicks.value credit) 0L "Zero speedup has zero credit."
                let _, state = prepare selected state
                let request, _ = prepare peer state
                Expect.isNone request "Zero speedup cannot request a positive delay."
                let selected, peer, state = start 50

                let first, state =
                    observe selected 1UL 1UL 0UL state
                    |> parsed

                let second, state =
                    observe selected 2UL 2UL 0UL state
                    |> parsed

                Expect.equal
                    (DelayTicks.value first, DelayTicks.value second)
                    (0L, 1L)
                    "Fractional nanoseconds survive consecutive periods."

                let _, state = prepare selected state
                let request, _ = required peer state

                Expect.equal
                    (SignalDelayRequest.nanoseconds request)
                    1L
                    "Small debt is not rounded to the request cap."

            testCase "Retirement requires no pending request and no unpaid debt"
            <| fun _ ->
                let _, peer, state = credited 80000UL

                Expect.isError
                    (SignalAccounting.unregister peer state)
                    "Unpaid debt prevents retirement."

                let request, state = required peer state

                Expect.isError
                    (SignalAccounting.unregister peer state)
                    "Reserved requests prevent retirement."

                let state = sent request 1UL state

                Expect.isError
                    (SignalAccounting.unregister peer state)
                    "Sent requests retain thread ownership."

                let state = complete request 1UL 40000L state

                let state =
                    SignalAccounting.unregister peer state
                    |> parsed

                Expect.isError
                    (SignalAccounting.prepare peer state)
                    "Retired handles cannot request more signals."

            testCase "Foreign handles and streams cannot affect another experiment"
            <| fun _ ->
                let selected, peer, state = credited 80000UL
                let otherSelected, otherPeer, other = credited 80000UL
                let foreignRequest, _ = required otherPeer other

                Expect.isError
                    (SignalAccounting.prepare otherPeer state)
                    "Ledger identities isolate experiments."

                let request, state = required peer state

                Expect.isError
                    (SignalAccounting.sent foreignRequest 1UL state)
                    "A foreign request cannot claim a pending target."

                let identity =
                    SignalAccounting.sampleIdentity otherSelected other
                    |> parsed

                let observation =
                    CpuSampleObservation.tryCreate
                        identity
                        2UL
                        100000UL
                        0UL
                        SampleThreadState.Runnable
                        SampleLocation.Selected
                    |> parsed

                Expect.isError
                    (SignalAccounting.observe selected observation state)
                    "Foreign sample streams cannot earn credit."

                Expect.isOk
                    (SignalAccounting.sent request 1UL state)
                    "Invalid inputs preserve the valid pending request."

                Expect.isError
                    (SignalAccounting.sampleIdentity peer state)
                    "Unsampled peers have no invented stream."

            testCase "Credit overflow does not consume its sample"
            <| fun _ ->
                let selected, _, state = start 100

                let _, state =
                    observe selected 1UL (uint64 Int64.MaxValue) 0UL state
                    |> parsed

                let _, state = prepare selected state

                Expect.isError
                    (observe
                        selected
                        2UL
                        (uint64 Int64.MaxValue
                         + 1UL)
                        0UL
                        state)
                    "Local credit cannot overflow."

                let credit, state =
                    observe selected 2UL (uint64 Int64.MaxValue) 0UL state
                    |> parsed

                Expect.equal
                    (DelayTicks.value credit)
                    0L
                    "A rejected observation leaves its sequence available."

                Expect.equal
                    (SignalAccounting.globalNanoseconds state)
                    Int64.MaxValue
                    "Overflow cannot alter published credit."

            testCase "New threads inherit current global delay"
            <| fun _ ->
                let _, _, state = credited 80000UL

                let newcomer, state =
                    SignalAccounting.register None state
                    |> parsed

                let request, state = prepare newcomer state

                Expect.isNone
                    request
                    "A new thread does not owe delay from before its registration."

                Expect.isOk
                    (SignalAccounting.unregister newcomer state)
                    "An untouched inherited thread is settled."

                for percent in
                    [
                        -1
                        101
                    ] do
                    Expect.isError
                        (SignalAccounting.register (Some percent) state)
                        "Invalid speedups cannot create sample streams."

            testCase "Wait overflow retains the pending request"
            <| fun _ ->
                let selected, peer, state = credited 80000UL
                let request, state = required peer state

                let state =
                    sent request 1UL state
                    |> complete request 1UL 40000L

                let _, state =
                    observe selected 2UL 100000UL 0UL state
                    |> parsed

                let _, state = prepare selected state
                let request, state = required peer state
                let state = sent request 2UL state

                Expect.isError
                    (SignalAccounting.complete
                        request
                        2UL
                        (SignalSettlement.Measured(ticks Int64.MaxValue))
                        state)
                    "Measured overshoot cannot wrap the local delay."

                Expect.equal
                    (SignalAccounting.pendingCount state)
                    1
                    "A failed accounting transition retains ownership."

                let state = complete request 2UL 10000L state

                Expect.isOk
                    (SignalAccounting.unregister peer state)
                    "A valid completion still settles the original request."

            testCase "Eligibility epoch overflow cannot start a signal request"
            <| fun _ ->
                let selected, peer, state = start 50

                let _, state =
                    observe selected 1UL 80000UL UInt64.MaxValue state
                    |> parsed

                let _, state =
                    observe selected 2UL 160000UL UInt64.MaxValue state
                    |> parsed

                let _, state = prepare selected state
                let request, state = required peer state

                let state =
                    sent request 1UL state
                    |> complete request 1UL 70000L

                let _, state = prepare peer state

                Expect.isError
                    (SignalAccounting.prepare selected state)
                    "A selected wait needs a fresh eligibility epoch."

                Expect.equal
                    (SignalAccounting.pendingCount state)
                    0
                    "Epoch overflow creates no partial reservation."

                let credit, _ =
                    observe selected 3UL 180000UL UInt64.MaxValue state
                    |> parsed

                Expect.equal
                    (DelayTicks.value credit)
                    10000L
                    "A failed reservation leaves the selected ledger runnable."
        ]
