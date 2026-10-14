namespace CozNet

open System

type internal SignalDelayRequest = private {
    Identity: Guid
    Thread: LedgerThread
    Nanoseconds: int64
}

[<RequireQualifiedAccess>]
module internal SignalDelayRequest =
    let identity request = request.Identity
    let thread request = request.Thread
    let nanoseconds request = request.Nanoseconds

[<RequireQualifiedAccess>]
type internal SignalSettlement =
    | Deferred
    | Measured of DelayTicks

type private SignalThreadAccounting = {
    Samples: SampleCreditStream option
    RequiredEpoch: uint64
    LastNativeSequence: uint64
}

[<RequireQualifiedAccess>]
type private SignalSubmission =
    | Reserved
    | Submitted
    | Sequenced of uint64

type private PendingSignal = {
    Request: SignalDelayRequest
    Submission: SignalSubmission
}

type private PendingService = {
    Lease: SignalServiceLease
    Sent: bool
    Count: int64
    Measured: int64
    Stopped: bool
    CancellationRequested: bool
}

type internal SignalAccounting = private {
    Ledger: DelayLedger
    Threads: Map<LedgerThread, SignalThreadAccounting>
    Pending: Map<LedgerThread, PendingSignal>
    Services: Map<LedgerThread, PendingService>
}

type internal SignalServiceProgress = {
    Count: int64
    LastSequence: uint64
    MeasuredNanoseconds: int64
}

[<RequireQualifiedAccess>]
module internal SignalAccounting =
    let create () = {
        Ledger = DelayLedger.create ()
        Threads = Map.empty
        Pending = Map.empty
        Services = Map.empty
    }

    let globalNanoseconds state = DelayLedger.globalTicks state.Ledger

    let pendingCount state =
        Map.count state.Pending
        + Map.count state.Services

    let private require message =
        function
        | Some value -> Ok value
        | None -> Error message

    let private find thread state =
        Map.tryFind thread state.Threads
        |> require "The signal thread is not registered."

    let requiredEpoch thread state =
        find thread state
        |> Result.map (fun entry -> entry.RequiredEpoch)

    let register samplePercent state =
        let samples =
            match samplePercent with
            | None -> Ok None
            | Some percent ->
                SampleCreditStream.tryCreate 1000000000L percent 0UL 0UL SampleThreadState.Runnable
                |> Result.map Some

        samples
        |> Result.bind (fun stream ->
            DelayLedger.register None state.Ledger
            |> Result.map (fun (thread, ledger) ->
                let entry = {
                    Samples = stream
                    RequiredEpoch = 0UL
                    LastNativeSequence = 0UL
                }

                thread,
                {
                    state with
                        Ledger = ledger
                        Threads = Map.add thread entry state.Threads
                }
            )
        )

    let sampleIdentity thread state =
        find thread state
        |> Result.bind (fun entry ->
            entry.Samples
            |> Option.map SampleCreditStream.identity
            |> require "The signal thread has no sample stream."
        )

    let observe thread observation state =
        find thread state
        |> Result.bind (fun entry ->
            if Map.containsKey thread state.Pending then
                Error "A pending signal excludes sample credit."
            elif CpuSampleObservation.eligibilityEpoch observation < entry.RequiredEpoch then
                Error "The sample predates the required signal eligibility epoch."
            else
                entry.Samples
                |> require "The signal thread has no sample stream."
                |> Result.bind (fun stream ->
                    SampleCreditStream.observe observation stream
                    |> Result.bind (fun (credit, samples) ->
                        DelayLedger.creditSample thread credit state.Ledger
                        |> Result.map (fun ledger ->
                            let next = {
                                entry with
                                    Samples = Some samples
                                    RequiredEpoch =
                                        CpuSampleObservation.eligibilityEpoch observation
                            }

                            credit,
                            {
                                state with
                                    Ledger = ledger
                                    Threads = Map.add thread next state.Threads
                            }
                        )
                    )
                )
        )

    let prepare thread state =
        find thread state
        |> Result.bind (fun entry ->
            DelayLedger.catchUp thread state.Ledger
            |> Result.bind (fun (action, ledger) ->
                match action with
                | DelayAction.Continue -> Ok(None, { state with Ledger = ledger })
                | DelayAction.Wait debt ->
                    if
                        entry.Samples.IsSome
                        && entry.RequiredEpoch = UInt64.MaxValue
                    then
                        Error "The signal eligibility epoch cannot advance."
                    else
                        let request = {
                            Identity = Guid.NewGuid()
                            Thread = thread
                            Nanoseconds = min 50000L (DelayTicks.value debt)
                        }

                        let next = {
                            entry with
                                RequiredEpoch =
                                    if entry.Samples.IsSome then
                                        entry.RequiredEpoch
                                        + 1UL
                                    else
                                        entry.RequiredEpoch
                        }

                        Ok(
                            Some request,
                            {
                                state with
                                    Ledger = ledger
                                    Threads = Map.add thread next state.Threads
                                    Pending =
                                        Map.add
                                            thread
                                            {
                                                Request = request
                                                Submission = SignalSubmission.Reserved
                                            }
                                            state.Pending
                            }
                        )
            )
        )

    let private findPending request state =
        match Map.tryFind request.Thread state.Pending with
        | Some pending when pending.Request = request -> Ok pending
        | _ -> Error "The signal request is stale or belongs to another accounting state."

    let sent request nativeSequence state =
        findPending request state
        |> Result.bind (fun pending ->
            let entry = state.Threads[request.Thread]

            if
                (match pending.Submission with
                 | SignalSubmission.Sequenced _ -> true
                 | _ -> false)
            then
                Error "The signal request already has a native sequence."
            elif
                nativeSequence
                <= entry.LastNativeSequence
            then
                Error "The native signal sequence must increase."
            else
                Ok {
                    state with
                        Threads =
                            Map.add
                                request.Thread
                                {
                                    entry with
                                        LastNativeSequence = nativeSequence
                                }
                                state.Threads
                        Pending =
                            Map.add
                                request.Thread
                                {
                                    pending with
                                        Submission = SignalSubmission.Sequenced nativeSequence
                                }
                                state.Pending
                }
        )

    let submitted request state =
        findPending request state
        |> Result.bind (fun pending ->
            match pending.Submission with
            | SignalSubmission.Reserved ->
                Ok {
                    state with
                        Pending =
                            Map.add
                                request.Thread
                                {
                                    pending with
                                        Submission = SignalSubmission.Submitted
                                }
                                state.Pending
                }
            | SignalSubmission.Submitted
            | SignalSubmission.Sequenced _ -> Error "The signal request was already submitted."
        )

    let isAwaitingSequence request state =
        findPending request state
        |> Result.map (fun pending -> pending.Submission = SignalSubmission.Submitted)

    let private settle request actual state =
        DelayLedger.completeDelay request.Thread actual state.Ledger
        |> Result.map (fun ledger -> {
            state with
                Ledger = ledger
                Pending = Map.remove request.Thread state.Pending
        })

    let rejectedBeforeSend request state =
        findPending request state
        |> Result.bind (fun pending ->
            match pending.Submission with
            | SignalSubmission.Submitted
            | SignalSubmission.Sequenced _ ->
                Error "A submitted signal requires its acknowledgment."
            | SignalSubmission.Reserved -> settle request DelayTicks.zero state
        )

    let complete request nativeSequence settlement state =
        findPending request state
        |> Result.bind (fun pending ->
            if
                pending.Submission
                <> SignalSubmission.Sequenced nativeSequence
            then
                Error "The acknowledgment does not match the pending native sequence."
            else
                let actual =
                    match settlement with
                    | SignalSettlement.Deferred -> DelayTicks.zero
                    | SignalSettlement.Measured duration -> duration

                settle request actual state
        )

    let private prepareServiceCore sampled thread state =
        find thread state
        |> Result.bind (fun entry ->
            if
                entry.Samples.IsSome
                && not sampled
            then
                Error "Only an unsampled peer can reserve a service lease."
            elif
                sampled
                && entry.Samples.IsNone
            then
                Error "Only a sampled thread can reserve a selected service lease."
            else
                DelayLedger.catchUp thread state.Ledger
                |> Result.bind (fun (action, ledger) ->
                    match action with
                    | DelayAction.Continue -> Ok(None, { state with Ledger = ledger })
                    | DelayAction.Wait debt ->
                        (if
                             sampled
                             && entry.RequiredEpoch = UInt64.MaxValue
                         then
                             Error "The signal eligibility epoch cannot advance."
                         else
                             SignalServiceLease.tryCreate thread (DelayTicks.value debt))
                        |> Result.map (fun lease ->
                            Some lease,
                            {
                                state with
                                    Ledger = ledger
                                    Threads =
                                        if sampled then
                                            Map.add
                                                thread
                                                {
                                                    entry with
                                                        RequiredEpoch =
                                                            entry.RequiredEpoch
                                                            + 1UL
                                                }
                                                state.Threads
                                        else
                                            state.Threads
                                    Services =
                                        Map.add
                                            thread
                                            {
                                                Lease = lease
                                                Sent = false
                                                Count = 0L
                                                Measured = 0L
                                                Stopped = false
                                                CancellationRequested = false
                                            }
                                            state.Services
                            }
                        )
                )
        )

    let prepareService thread state = prepareServiceCore false thread state

    let prepareSelectedService thread state = prepareServiceCore true thread state

    let private findService lease state =
        match Map.tryFind (SignalServiceLease.thread lease) state.Services with
        | Some service when service.Lease = lease -> Ok service
        | _ -> Error "The service lease is stale or belongs to another accounting state."

    let sentService lease state =
        findService lease state
        |> Result.bind (fun service ->
            if service.Sent then
                Error "The service lease was already submitted."
            else
                Ok {
                    state with
                        Services =
                            Map.add
                                (SignalServiceLease.thread lease)
                                { service with Sent = true }
                                state.Services
                }
        )

    let confirmedServiceNanoseconds lease state =
        findService lease state
        |> Result.map (fun service -> service.Measured)

    let serviceProgress lease state =
        findService lease state
        |> Result.map (fun service -> {
            Count = service.Count
            LastSequence =
                if service.Count = 0L then
                    0UL
                else
                    state.Threads[SignalServiceLease.thread lease].LastNativeSequence
            MeasuredNanoseconds = service.Measured
        })

    let acceptServiceChild lease child state =
        findService lease state
        |> Result.bind (fun service ->
            let thread = SignalServiceLease.thread lease
            let sequence = SignalServiceChild.sequence child
            let measured = SignalServiceChild.measured child

            let remaining =
                SignalServiceLease.nanoseconds lease
                - service.Measured

            if
                not service.Sent
                || service.Stopped
            then
                Error "The service lease cannot accept another child."
            elif
                SignalServiceChild.leaseIdentity child
                <> SignalServiceLease.identity lease
                || SignalServiceChild.ordinal child
                   <> service.Count
                      + 1L
                || sequence
                   <= state.Threads[thread].LastNativeSequence
            then
                Error "The service child does not extend the accepted journal."
            elif
                SignalServiceChild.requested child
                <> min 50000L remaining
            then
                Error "The service child does not match the remaining snapshot debt."
            elif
                measured > Int64.MaxValue
                           - service.Measured
            then
                Error "Service wait accounting overflow."
            else
                let total =
                    service.Measured
                    + measured

                let next = {
                    service with
                        Count =
                            service.Count
                            + 1L
                        Measured = total
                        Stopped =
                            SignalServiceChild.stops child
                            || total
                               >= SignalServiceLease.nanoseconds lease
                }

                Ok {
                    state with
                        Services = Map.add thread next state.Services
                        Threads =
                            Map.add
                                thread
                                {
                                    state.Threads[thread] with
                                        LastNativeSequence = sequence
                                }
                                state.Threads
                }
        )

    let private releaseService lease service state =
        let thread = SignalServiceLease.thread lease

        DelayTicks.tryCreate service.Measured
        |> Result.bind (fun measured ->
            DelayLedger.completeDelay thread measured state.Ledger
            |> Result.map (fun ledger ->
                measured,
                {
                    state with
                        Ledger = ledger
                        Services = Map.remove thread state.Services
                }
            )
        )

    let completeService lease state =
        findService lease state
        |> Result.bind (fun service ->
            if
                not service.Sent
                || not service.Stopped
            then
                Error "The service journal has no terminal child."
            else
                releaseService lease service state
        )

    let requestServiceCancellation lease state =
        findService lease state
        |> Result.bind (fun service ->
            if not service.Sent then
                Error "An unsubmitted service lease requires guaranteed rejection."
            else
                Ok {
                    state with
                        Services =
                            Map.add
                                (SignalServiceLease.thread lease)
                                {
                                    service with
                                        CancellationRequested = true
                                }
                                state.Services
                }
        )

    let confirmServiceStopped lease receipt state =
        findService lease state
        |> Result.bind (fun service ->
            let expectedSequence =
                if service.Count = 0L then
                    0UL
                else
                    state.Threads[SignalServiceLease.thread lease].LastNativeSequence

            if
                not service.Sent
                || not service.CancellationRequested
            then
                Error "The service lease has no submitted cancellation."
            elif
                SignalServiceStopReceipt.leaseIdentity receipt
                <> SignalServiceLease.identity lease
                || SignalServiceStopReceipt.count receipt
                   <> service.Count
                || SignalServiceStopReceipt.sequence receipt
                   <> expectedSequence
                || SignalServiceStopReceipt.measured receipt
                   <> service.Measured
            then
                Error "The service stop receipt does not match the accepted journal."
            else
                releaseService lease service state
        )

    let rejectedServiceBeforeSend lease state =
        findService lease state
        |> Result.bind (fun service ->
            if service.Sent then
                Error "A submitted service lease still owns its delivery."
            else
                releaseService lease service state
                |> Result.map snd
        )

    let unregister thread state =
        DelayLedger.unregister thread state.Ledger
        |> Result.map (fun ledger -> {
            state with
                Ledger = ledger
                Threads = Map.remove thread state.Threads
        })
