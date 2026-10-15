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

    let private service peer state =
        let lease, next =
            SignalAccounting.prepareService peer state
            |> parsed

        Expect.isSome lease "The peer requires a debt reservation."
        Option.get lease, next

    let private child lease ordinal sequence requested measured outcome =
        SignalServiceChild.tryCreate
            (SignalServiceLease.identity lease)
            ordinal
            sequence
            requested
            measured
            measured
            1000000L
            outcome
            true
        |> parsed

    let private accept lease ordinal sequence requested measured outcome state =
        SignalAccounting.acceptServiceChild
            lease
            (child lease ordinal sequence requested measured outcome)
            state
        |> parsed

    [<Tests>]
    let tests =
        testList "Signal accounting" [
            testCase "Thread snapshot resets empty prefix sequence on reused service"
            <| fun _ ->
                let selected, peer, state = credited 100000UL
                let first, reserved = service peer state

                let terminal =
                    SignalAccounting.sentService first reserved
                    |> parsed
                    |> accept first 1L 7UL 50000L 50000L 2

                let _, released =
                    SignalAccounting.completeService first terminal
                    |> parsed

                let _, creditedAgain =
                    observe selected 2UL 200000UL 0UL released
                    |> parsed

                let _, published = prepare selected creditedAgain
                let next, reservedAgain = service peer published

                let snapshot =
                    SignalAccounting.captureThread peer reservedAgain
                    |> parsed

                Expect.notEqual
                    (SignalServiceLease.identity next)
                    (SignalServiceLease.identity first)
                    "The replacement must own a new service identity."

                Expect.equal
                    snapshot.Operation
                    (SignalOperationSnapshot.Service(
                        next,
                        {
                            Count = 0L
                            LastSequence = 0UL
                            MeasuredNanoseconds = 0L
                        }
                    ))
                    "An empty replacement prefix must not inherit the previous native sequence."

                Expect.equal
                    (DelayTicks.value snapshot.Ledger.Local)
                    50000L
                    "A new reservation must retain the previous settled payment."

            testCase
                "Thread snapshot separates accepted service wait from settlement and publication"
            <| fun _ ->
                let _, peer, state = credited 200000UL
                let lease, reserved = service peer state

                let active =
                    SignalAccounting.sentService lease reserved
                    |> parsed
                    |> accept lease 1L 1UL 50000L 50000L 2

                let frozen =
                    SignalAccounting.captureThread peer active
                    |> parsed

                let terminal =
                    active
                    |> accept lease 2L 2UL 50000L 55000L 2

                let _, settled =
                    SignalAccounting.completeService lease terminal
                    |> parsed

                let completed =
                    SignalAccounting.captureThread peer settled
                    |> parsed

                let _, published = prepare peer settled

                let after =
                    SignalAccounting.captureThread peer published
                    |> parsed

                Expect.equal
                    frozen.Operation
                    (SignalOperationSnapshot.Service(
                        lease,
                        {
                            Count = 1L
                            LastSequence = 1UL
                            MeasuredNanoseconds = 50000L
                        }
                    ))
                    "The frozen service must retain its accepted prefix and lease."

                Expect.equal
                    frozen.Ledger.Phase
                    LedgerThreadPhase.Delaying
                    "Accepted wait must not release the ledger."

                Expect.equal
                    (DelayTicks.value frozen.Ledger.Local)
                    0L
                    "Accepted service wait is not settled local credit."

                Expect.equal
                    (DelayTicks.value frozen.Ledger.Global)
                    100000L
                    "Observation must not publish accepted wait."

                Expect.equal
                    completed.Operation
                    SignalOperationSnapshot.Idle
                    "Settlement must remove unsettled service wait."

                Expect.equal
                    (DelayTicks.value completed.Ledger.Local)
                    105000L
                    "The full accepted sum must enter local credit once."

                Expect.equal
                    (DelayTicks.value completed.Ledger.Global)
                    100000L
                    "Settlement must leave overshoot unpublished."

                Expect.equal
                    (DelayTicks.value after.Ledger.Global)
                    105000L
                    "Catch-up must publish overshoot separately."

                Expect.equal
                    (SignalAccounting.captureThread peer active
                     |> parsed)
                    frozen
                    "Later transitions must preserve the frozen snapshot."

            testCase "Thread snapshot retains reservation and individual request ownership"
            <| fun _ ->
                let _, peer, state = credited 100000UL

                let idle =
                    SignalAccounting.captureThread peer state
                    |> parsed

                let lease, reserved = service peer state

                let snapshot =
                    SignalAccounting.captureThread peer reserved
                    |> parsed

                let rejected =
                    SignalAccounting.rejectedServiceBeforeSend lease reserved
                    |> parsed

                let request, pending = required peer rejected

                let requestSnapshot =
                    SignalAccounting.captureThread peer pending
                    |> parsed

                Expect.equal
                    idle.Operation
                    SignalOperationSnapshot.Idle
                    "Unpaid debt alone must not invent ownership."

                Expect.equal
                    snapshot.Operation
                    (SignalOperationSnapshot.Service(
                        lease,
                        {
                            Count = 0L
                            LastSequence = 0UL
                            MeasuredNanoseconds = 0L
                        }
                    ))
                    "An unsubmitted reservation must retain its identity with an empty prefix."

                Expect.equal
                    requestSnapshot.Operation
                    (SignalOperationSnapshot.Request request)
                    "An individual request must not become an idle or service state."

                Expect.equal
                    requestSnapshot.Ledger.Phase
                    LedgerThreadPhase.Delaying
                    "The request must retain delaying state."

                Expect.equal
                    (DelayTicks.value requestSnapshot.Ledger.Global)
                    50000L
                    "Rejection must not forgive unpaid debt."

            testCase "Thread snapshot rejects foreign registration"
            <| fun _ ->
                let _, peer, _ = start 50
                let _, _, other = start 50

                Expect.equal
                    (SignalAccounting.captureThread peer other)
                    (Error "The signal thread is not registered.")
                    "Snapshot lookup must preserve signal registration ownership."

            testCase "Selected service pays peer overshoot and excludes stale samples"
            <| fun _ ->
                let selected, peer, state = credited 100000UL
                let lease, state = service peer state

                let state =
                    SignalAccounting.sentService lease state
                    |> parsed
                    |> accept lease 1L 1UL 50000L 70000L 2

                let _, state =
                    SignalAccounting.completeService lease state
                    |> parsed

                let _, state =
                    SignalAccounting.prepareService peer state
                    |> parsed

                let selectedLease, reserved =
                    SignalAccounting.prepareSelectedService selected state
                    |> parsed

                let selectedLease = Option.get selectedLease

                Expect.equal
                    (SignalServiceLease.nanoseconds selectedLease)
                    20000L
                    "The selected worker owes the full peer overshoot."

                Expect.equal
                    (SignalAccounting.requiredEpoch selected reserved
                     |> parsed)
                    1UL
                    "Reservation excludes the old sampling epoch."

                Expect.isError
                    (observe selected 2UL 120000UL 0UL reserved)
                    "Old samples cannot credit a pending payment."

                Expect.isError
                    (observe selected 2UL 120000UL 1UL reserved)
                    "Even fresh samples cannot credit a delaying worker."

                Expect.isError
                    (SignalAccounting.prepareSelectedService selected reserved)
                    "A reservation cannot be replaced."

                let settled =
                    SignalAccounting.sentService selectedLease reserved
                    |> parsed
                    |> accept selectedLease 1L 1UL 20000L 21000L 2

                let measured, settled =
                    SignalAccounting.completeService selectedLease settled
                    |> parsed

                Expect.equal
                    (DelayTicks.value measured)
                    21000L
                    "Settlement retains selected overshoot."

                Expect.equal
                    (SignalAccounting.globalNanoseconds settled)
                    70000L
                    "Settlement does not publish overshoot early."

                let next, settled =
                    SignalAccounting.prepareSelectedService selected settled
                    |> parsed

                Expect.isNone next "The selected worker has paid its debt."

                Expect.equal
                    (SignalAccounting.globalNanoseconds settled)
                    71000L
                    "Selected catch-up publishes actual wait."

                Expect.equal
                    (SignalAccounting.pendingCount settled)
                    0
                    "The selected reservation settles exactly once."

                Expect.isError
                    (SignalAccounting.completeService selectedLease settled)
                    "Repeated settlement must fail."

                Expect.isError
                    (observe selected 2UL 120000UL 0UL settled)
                    "Stale samples remain invalid after settlement."

                let baseline, settled =
                    observe selected 2UL 120000UL 1UL settled
                    |> parsed

                Expect.equal
                    (DelayTicks.value baseline)
                    0L
                    "The fresh epoch establishes a baseline."

                let credit, _ =
                    observe selected 3UL 140000UL 1UL settled
                    |> parsed

                Expect.equal
                    (DelayTicks.value credit)
                    10000L
                    "Only CPU after the new baseline earns credit."

            testCase "Selected service deferral and rejection preserve unpaid debt"
            <| fun _ ->
                let selected, peer, state = credited 100000UL
                let request, state = required peer state

                let state =
                    sent request 1UL state
                    |> complete request 1UL 70000L

                let _, state = prepare peer state

                let lease, state =
                    SignalAccounting.prepareSelectedService selected state
                    |> parsed

                let lease = Option.get lease

                let rejected =
                    SignalAccounting.rejectedServiceBeforeSend lease state
                    |> parsed

                let retry, state =
                    SignalAccounting.prepareSelectedService selected rejected
                    |> parsed

                let retry = Option.get retry

                Expect.equal
                    (SignalServiceLease.nanoseconds retry)
                    20000L
                    "Rejected delivery does not forgive debt."

                let state =
                    SignalAccounting.sentService retry state
                    |> parsed
                    |> accept retry 1L 1UL 20000L 0L 1

                let measured, state =
                    SignalAccounting.completeService retry state
                    |> parsed

                Expect.equal (DelayTicks.value measured) 0L "Deferral cannot credit a wait."

                let retry, state =
                    SignalAccounting.prepareSelectedService selected state
                    |> parsed

                Expect.equal
                    (SignalServiceLease.nanoseconds (Option.get retry))
                    20000L
                    "Deferral preserves the full debt."

                Expect.equal
                    (SignalAccounting.requiredEpoch selected state
                     |> parsed)
                    3UL
                    "Every reservation advances the epoch."

            testCase "Selected service preserves role and debt bounds"
            <| fun _ ->
                let selected, peer, state = credited 100000UL

                Expect.isError
                    (SignalAccounting.prepareSelectedService peer state)
                    "An unsampled peer cannot use the selected path."

                let none, unchanged =
                    SignalAccounting.prepareSelectedService selected state
                    |> parsed

                Expect.isNone none "A settled selected worker needs no lease."

                Expect.equal
                    (SignalAccounting.requiredEpoch selected unchanged
                     |> parsed)
                    0UL
                    "No payment leaves the epoch unchanged."

                let request, state = required peer state

                let state =
                    sent request 1UL state
                    |> complete request 1UL 2050001L

                let _, state = prepare peer state

                Expect.isError
                    (SignalAccounting.prepareSelectedService selected state)
                    "Selected debt above 2 ms must fail instead of being clipped."

                Expect.equal
                    (SignalAccounting.pendingCount state)
                    0
                    "An invalid budget cannot partially reserve the selected worker."

            testCase "Submitted selected request retains ownership until sequence binding"
            <| fun _ ->
                let selected, peer, state = credited 80000UL
                let peerRequest, state = required peer state

                let state =
                    sent peerRequest 1UL state
                    |> complete peerRequest 1UL 70000L

                let _, state = prepare peer state
                let request, reserved = required selected state

                let epoch =
                    SignalAccounting.requiredEpoch selected reserved
                    |> parsed

                let submitted =
                    SignalAccounting.submitted request reserved
                    |> parsed

                Expect.notEqual
                    (SignalDelayRequest.identity request)
                    Guid.Empty
                    "Native correlation requires a request identity."

                Expect.equal
                    (SignalAccounting.pendingCount submitted)
                    1
                    "Submission retains the selected reservation."

                Expect.isError
                    (SignalAccounting.rejectedBeforeSend request submitted)
                    "Submission can deliver work before its sequence is observed."

                Expect.isError
                    (SignalAccounting.submitted request submitted)
                    "A second submission cannot claim the same request."

                Expect.isError
                    (SignalAccounting.complete request 2UL SignalSettlement.Deferred submitted)
                    "An unbound sequence cannot complete a request."

                Expect.isError
                    (SignalAccounting.unregister selected submitted)
                    "Submission retains the registration."

                Expect.isError
                    (observe selected 2UL 100000UL epoch submitted)
                    "The selected target cannot earn credit during service."

                Expect.isError
                    (SignalAccounting.sent request 0UL submitted)
                    "A zero native sequence cannot bind submission."

                let bound = sent request 2UL submitted

                Expect.isError
                    (SignalAccounting.submitted request bound)
                    "A bound request cannot be submitted again."

                let settled = complete request 2UL 30000L bound

                Expect.equal
                    (SignalAccounting.pendingCount settled)
                    0
                    "The matched acknowledgment releases ownership."

                let request, settled = prepare selected settled
                Expect.isNone request "The measured wait pays the selected debt."

                let credit, _ =
                    observe selected 2UL 100000UL epoch settled
                    |> parsed

                Expect.equal
                    (DelayTicks.value credit)
                    0L
                    "The first resumed sample establishes the new epoch baseline."

            testCase "Submitted request rejects foreign and reused sequence bindings"
            <| fun _ ->
                let selected, peer, state = credited 80000UL
                let first, state = required peer state

                let state =
                    sent first 7UL state
                    |> complete first 7UL 20000L

                let request, reserved = required peer state

                let submitted =
                    SignalAccounting.submitted request reserved
                    |> parsed

                Expect.isError
                    (SignalAccounting.submitted first reserved)
                    "A stale request cannot submit the current reservation."

                Expect.isError
                    (SignalAccounting.sent request 7UL submitted)
                    "Submission cannot reuse a native sequence."

                Expect.isError
                    (SignalAccounting.sent request 6UL submitted)
                    "Submission cannot move the native sequence backward."

                let _, otherPeer, other = credited 80000UL
                let foreign, _ = required otherPeer other

                Expect.isError
                    (SignalAccounting.submitted foreign reserved)
                    "Foreign requests cannot submit another experiment's reservation."

                let _, advanced =
                    observe selected 2UL 100000UL 0UL submitted
                    |> parsed

                let _, advanced = prepare selected advanced

                let settled =
                    sent request 8UL advanced
                    |> complete request 8UL 20000L

                let remaining, _ = required peer settled

                Expect.equal
                    (SignalDelayRequest.nanoseconds remaining)
                    10000L
                    "Submission preserves credit that arrives before sequence binding."

                Expect.isOk
                    (SignalAccounting.rejectedBeforeSend request reserved)
                    "A never-submitted reservation still permits guaranteed rejection."

            testCase "Peer service journal retains new credit and settles its aggregate once"
            <| fun _ ->
                let selected, peer, state = credited 220000UL
                let lease, state = service peer state

                Expect.equal
                    (SignalServiceLease.nanoseconds lease)
                    110000L
                    "The lease captures the whole original peer debt."

                let state =
                    SignalAccounting.sentService lease state
                    |> parsed
                    |> accept lease 1L 1UL 50000L 50000L 2

                let _, state =
                    observe selected 2UL 300000UL 0UL state
                    |> parsed

                let _, state = prepare selected state

                let state =
                    state
                    |> accept lease 2L 2UL 50000L 50000L 2
                    |> accept lease 3L 3UL 10000L 15000L 2

                Expect.equal
                    (SignalAccounting.confirmedServiceNanoseconds lease state
                     |> parsed)
                    115000L
                    "The confirmed prefix includes terminal overshoot."

                Expect.equal
                    (SignalAccounting.pendingCount state)
                    1
                    "The ledger stays reserved until aggregate completion."

                let measured, state =
                    SignalAccounting.completeService lease state
                    |> parsed

                Expect.equal
                    (DelayTicks.value measured)
                    115000L
                    "The committed amount is the complete journal sum."

                Expect.equal
                    (SignalAccounting.pendingCount state)
                    0
                    "Completion removes the diagnostic prefix with the reservation."

                Expect.isError
                    (SignalAccounting.completeService lease state)
                    "A duplicate terminal result cannot settle twice."

                let request, _ = required peer state

                Expect.equal
                    (SignalDelayRequest.nanoseconds request)
                    35000L
                    "New global credit belongs to later service."

            testCase "Service rejection preserves ownership before and after submission"
            <| fun _ ->
                let _, peer, state = credited 100000UL
                let lease, reserved = service peer state

                Expect.isError
                    (SignalAccounting.acceptServiceChild
                        lease
                        (child lease 1L 1UL 50000L 50000L 2)
                        reserved)
                    "Unsubmitted leases cannot receive child records."

                let rejected =
                    SignalAccounting.rejectedServiceBeforeSend lease reserved
                    |> parsed

                Expect.equal
                    (SignalAccounting.pendingCount rejected)
                    0
                    "Guaranteed rejection releases the reservation."

                let replacement, reserved = service peer rejected

                Expect.notEqual
                    (SignalServiceLease.identity replacement)
                    (SignalServiceLease.identity lease)
                    "A replacement lease needs a new identity."

                Expect.isError
                    (SignalAccounting.sentService lease reserved)
                    "An old lease cannot bind a replacement reservation."

                let submitted =
                    SignalAccounting.sentService replacement reserved
                    |> parsed

                Expect.isError
                    (SignalAccounting.sentService replacement submitted)
                    "Submission cannot bind twice."

                Expect.isError
                    (SignalAccounting.rejectedServiceBeforeSend replacement submitted)
                    "Ambiguous delivery must retain ownership."

                Expect.isError
                    (SignalAccounting.completeService replacement submitted)
                    "An empty journal cannot prove completion."

                Expect.isError
                    (SignalAccounting.unregister peer submitted)
                    "The peer cannot retire while service owns it."

                Expect.isError
                    (SignalAccounting.prepare peer submitted)
                    "Single requests cannot overlap a lease."

            testCase "Service journal rejects foreign duplicate skipped and replayed children"
            <| fun _ ->
                let _, peer, state = credited 220000UL
                let lease, state = service peer state

                let state =
                    SignalAccounting.sentService lease state
                    |> parsed
                    |> accept lease 1L 9UL 50000L 50000L 2

                let _, otherPeer, other = credited 220000UL
                let foreign, _ = service otherPeer other

                for receipt in
                    [
                        child foreign 2L 10UL 50000L 50000L 2
                        child lease 1L 9UL 50000L 50000L 2
                        child lease 3L 10UL 50000L 50000L 2
                        child lease 2L 9UL 50000L 50000L 2
                        child lease 2L 10UL 40000L 40000L 2
                    ] do
                    Expect.isError
                        (SignalAccounting.acceptServiceChild lease receipt state)
                        "Only the next matching child can extend the prefix."

                Expect.equal
                    (SignalAccounting.confirmedServiceNanoseconds lease state
                     |> parsed)
                    50000L
                    "Rejected records leave the last prefix intact."

                let next = accept lease 2L 10UL 50000L 50000L 2 state

                Expect.equal
                    (SignalAccounting.confirmedServiceNanoseconds lease next
                     |> parsed)
                    100000L
                    "A valid retry extends the unchanged prefix."

            testCase "Deferred and interrupted service children terminate with prior measured waits"
            <| fun _ ->
                for outcome, partial in
                    [
                        1, 0L
                        3, 10000L
                    ] do
                    let _, peer, state = credited 220000UL
                    let lease, state = service peer state

                    let state =
                        SignalAccounting.sentService lease state
                        |> parsed
                        |> accept lease 1L 1UL 50000L 50000L 2
                        |> accept lease 2L 2UL 50000L partial outcome

                    Expect.isError
                        (SignalAccounting.acceptServiceChild
                            lease
                            (child lease 3L 3UL 50000L 50000L 2)
                            state)
                        "A terminal child must stop the native chain."

                    let measured, state =
                        SignalAccounting.completeService lease state
                        |> parsed

                    Expect.equal
                        (DelayTicks.value measured)
                        (50000L
                         + partial)
                        "Termination preserves earlier and partial measured waits."

                    let next, _ = service peer state

                    Expect.equal
                        (SignalServiceLease.nanoseconds next)
                        (60000L
                         - partial)
                        "Unpaid snapshot debt remains after partial service."

            testCase "Service eligibility and capacity failures do not consume the original state"
            <| fun _ ->
                let selected, peer, empty = start 50

                let none, unchanged =
                    SignalAccounting.prepareService peer empty
                    |> parsed

                Expect.isNone none "A settled peer needs no lease."

                Expect.equal
                    (SignalAccounting.pendingCount unchanged)
                    0
                    "An empty check reserves nothing."

                Expect.isError
                    (SignalAccounting.prepareService selected empty)
                    "Selected service keeps its existing sample cadence."

                let _, peer, large = credited 4000002UL

                Expect.isError
                    (SignalAccounting.prepareService peer large)
                    "Debt above 2 ms must fail instead of being clipped."

                let request, _ = required peer large

                Expect.equal
                    (SignalDelayRequest.nanoseconds request)
                    50000L
                    "The rejected lease leaves its input ledger usable."

            testCase "Service terminal overshoot publishes only at ordinary catch-up"
            <| fun _ ->
                let _, peer, state = credited 100000UL
                let lease, state = service peer state

                let state =
                    SignalAccounting.sentService lease state
                    |> parsed
                    |> accept lease 1L 1UL 50000L 70000L 2

                let _, state =
                    SignalAccounting.completeService lease state
                    |> parsed

                Expect.equal
                    (SignalAccounting.globalNanoseconds state)
                    50000L
                    "Completion must not publish overshoot early."

                let next, state =
                    SignalAccounting.prepareService peer state
                    |> parsed

                Expect.isNone next "Overshoot leaves no peer debt."

                Expect.equal
                    (SignalAccounting.globalNanoseconds state)
                    70000L
                    "Catch-up publishes actual measured overshoot."

            testCase "Service child parser enforces timing amount and outcome boundaries"
            <| fun _ ->
                let identity = Guid.NewGuid()

                let parse ordinal sequence request wait handler observed outcome eligible =
                    SignalServiceChild.tryCreate
                        identity
                        ordinal
                        sequence
                        request
                        wait
                        handler
                        observed
                        outcome
                        eligible

                Expect.isOk
                    (parse 100000L UInt64.MaxValue 50000L 5000000L 5000000L 100000000L 2 true)
                    "Exact upper bounds and measured overshoot remain valid."

                for result in
                    [
                        parse 0L 1UL 1L 0L 0L 0L 2 true
                        parse 100001L 1UL 1L 0L 0L 0L 2 true
                        parse 1L 0UL 1L 0L 0L 0L 2 true
                        parse 1L 1UL 0L 0L 0L 0L 2 true
                        parse 1L 1UL 50001L 0L 0L 0L 2 true
                        parse 1L 1UL 1L -1L 0L 0L 2 true
                        parse 1L 1UL 1L 2L 1L 2L 2 true
                        parse 1L 1UL 1L 0L 5000001L 5000001L 2 true
                        parse 1L 1UL 1L 0L 0L 100000001L 2 true
                        parse 1L 1UL 1L 0L 2L 1L 2 true
                        parse 1L 1UL 1L 1L 1L 1L 1 true
                        parse 1L 1UL 1L 0L 0L 0L 4 true
                        parse 1L 1UL 1L 0L 0L 0L 2 false
                    ] do
                    Expect.isError result "Invalid transport records must fail before accounting."

            testCase "Service aggregate overflow retains the terminal reservation"
            <| fun _ ->
                let selected, state =
                    SignalAccounting.register (Some 100) (SignalAccounting.create ())
                    |> parsed

                let _, state =
                    observe
                        selected
                        1UL
                        (uint64 (
                            Int64.MaxValue
                            - 2L
                        ))
                        0UL
                        state
                    |> parsed

                let _, state = prepare selected state

                let peer, state =
                    SignalAccounting.register None state
                    |> parsed

                let _, state =
                    observe selected 2UL (uint64 Int64.MaxValue) 0UL state
                    |> parsed

                let _, state = prepare selected state
                let lease, state = service peer state

                let state =
                    SignalAccounting.sentService lease state
                    |> parsed
                    |> accept lease 1L 1UL 2L 3L 2

                Expect.isError
                    (SignalAccounting.completeService lease state)
                    "Measured overshoot cannot wrap the local ledger counter."

                Expect.equal
                    (SignalAccounting.pendingCount state)
                    1
                    "A failed aggregate commit must retain ownership."

                Expect.equal
                    (SignalAccounting.confirmedServiceNanoseconds lease state
                     |> parsed)
                    3L
                    "The accepted journal remains available after failure."

                Expect.equal
                    (SignalAccounting.globalNanoseconds state)
                    Int64.MaxValue
                    "A failed commit cannot change global delay."

            testCase "Service cancellation retains ownership until a matching stop receipt"
            <| fun _ ->
                let _, peer, state = credited 220000UL
                let lease, reserved = service peer state

                Expect.isError
                    (SignalAccounting.requestServiceCancellation lease reserved)
                    "Unsubmitted leases use guaranteed rejection."

                let submitted =
                    SignalAccounting.sentService lease reserved
                    |> parsed

                let empty =
                    SignalServiceStopReceipt.tryCreate
                        (SignalServiceLease.identity lease)
                        0L
                        0UL
                        0L
                        0
                    |> parsed

                Expect.isError
                    (SignalAccounting.confirmServiceStopped lease empty submitted)
                    "An unsolicited stop cannot release a lease."

                let stopping =
                    SignalAccounting.requestServiceCancellation lease submitted
                    |> parsed

                Expect.equal
                    (SignalAccounting.requestServiceCancellation lease stopping
                     |> parsed)
                    stopping
                    "Repeated cancellation does not alter ownership."

                Expect.equal
                    (SignalAccounting.pendingCount stopping)
                    1
                    "Requesting cancellation does not settle unobserved work."

                Expect.isError
                    (SignalAccounting.unregister peer stopping)
                    "Cancellation intent does not permit target retirement."

                let total, released =
                    SignalAccounting.confirmServiceStopped lease empty stopping
                    |> parsed

                Expect.equal (DelayTicks.value total) 0L "A stopped empty journal pays no debt."

                Expect.equal
                    (SignalAccounting.pendingCount released)
                    0
                    "A matching empty stop releases ownership."

                Expect.isError
                    (SignalAccounting.confirmServiceStopped lease empty released)
                    "Stop receipts cannot release a lease twice."

                let next, _ = service peer released

                Expect.equal
                    (SignalServiceLease.nanoseconds next)
                    110000L
                    "Cancellation without a child preserves the complete debt."

            testCase
                "Service cancellation accepts the final in-flight child before committing its prefix"
            <| fun _ ->
                let _, peer, state = credited 220000UL
                let lease, state = service peer state

                let state =
                    SignalAccounting.sentService lease state
                    |> parsed
                    |> accept lease 1L 8UL 50000L 50000L 2

                let stopping =
                    SignalAccounting.requestServiceCancellation lease state
                    |> parsed

                let final = accept lease 2L 9UL 50000L 50000L 2 stopping

                let stop =
                    SignalServiceStopReceipt.tryCreate
                        (SignalServiceLease.identity lease)
                        2L
                        9UL
                        100000L
                        0
                    |> parsed

                Expect.isError
                    (SignalAccounting.confirmServiceStopped lease stop stopping)
                    "The stop cannot skip a child that the managed owner has not accepted."

                let measured, released =
                    SignalAccounting.confirmServiceStopped lease stop final
                    |> parsed

                Expect.equal
                    (DelayTicks.value measured)
                    100000L
                    "The final in-flight wait remains in the committed journal."

                Expect.isError
                    (SignalAccounting.acceptServiceChild
                        lease
                        (child lease 3L 10UL 10000L 10000L 2)
                        released)
                    "A closed lease cannot accept more work."

                let next, _ = service peer released

                Expect.equal
                    (SignalServiceLease.nanoseconds next)
                    10000L
                    "Cancellation leaves only the unpaid remainder."

            testCase "Service stop receipt must match every accepted journal field"
            <| fun _ ->
                let _, peer, state = credited 220000UL
                let lease, state = service peer state

                let state =
                    SignalAccounting.sentService lease state
                    |> parsed
                    |> accept lease 1L 12UL 50000L 50000L 2

                let stopping =
                    SignalAccounting.requestServiceCancellation lease state
                    |> parsed

                let identity = SignalServiceLease.identity lease

                for id, count, sequence, measured in
                    [
                        (Guid.NewGuid(), 1L, 12UL, 50000L)
                        (identity, 2L, 12UL, 50000L)
                        (identity, 1L, 13UL, 50000L)
                        (identity, 1L, 12UL, 50001L)
                    ] do
                    let receipt =
                        SignalServiceStopReceipt.tryCreate id count sequence measured 0
                        |> parsed

                    Expect.isError
                        (SignalAccounting.confirmServiceStopped lease receipt stopping)
                        "Mismatched evidence cannot release the reservation."

                Expect.equal
                    (SignalAccounting.confirmedServiceNanoseconds lease stopping
                     |> parsed)
                    50000L
                    "Rejected stop receipts preserve the accepted prefix."

                Expect.equal
                    (SignalAccounting.pendingCount stopping)
                    1
                    "Rejected stop receipts retain ownership."

            testCase "Service stop parser rejects pending children and inconsistent empty journals"
            <| fun _ ->
                let identity = Guid.NewGuid()

                for id, count, sequence, measured, outstanding in
                    [
                        (Guid.Empty, 0L, 0UL, 0L, 0)
                        (identity, -1L, 0UL, 0L, 0)
                        (identity, 100001L, 1UL, 0L, 0)
                        (identity, 0L, 1UL, 0L, 0)
                        (identity, 0L, 0UL, 1L, 0)
                        (identity, 1L, 0UL, 0L, 0)
                        (identity, 1L, 1UL, -1L, 0)
                        (identity, 0L, 0UL, 0L, 1)
                        (identity, 0L, 0UL, 0L, -1)
                    ] do
                    Expect.isError
                        (SignalServiceStopReceipt.tryCreate id count sequence measured outstanding)
                        "Only a consistent journal with no outstanding child can report a stop."

                Expect.isOk
                    (SignalServiceStopReceipt.tryCreate identity 100000L UInt64.MaxValue 0L 0)
                    "A nonempty zero-wait journal remains representable."

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

                Expect.isError
                    (SignalAccounting.prepareSelectedService selected state)
                    "A selected service also needs a fresh eligibility epoch."

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
