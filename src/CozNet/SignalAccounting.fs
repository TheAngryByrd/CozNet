namespace CozNet

open System

type internal SignalDelayRequest = private {
    Identity: Guid
    Thread: LedgerThread
    Nanoseconds: int64
}

[<RequireQualifiedAccess>]
module internal SignalDelayRequest =
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

type private PendingSignal = {
    Request: SignalDelayRequest
    NativeSequence: uint64 option
}

type internal SignalAccounting = private {
    Ledger: DelayLedger
    Threads: Map<LedgerThread, SignalThreadAccounting>
    Pending: Map<LedgerThread, PendingSignal>
}

[<RequireQualifiedAccess>]
module internal SignalAccounting =
    let create () = {
        Ledger = DelayLedger.create ()
        Threads = Map.empty
        Pending = Map.empty
    }

    let globalNanoseconds state =
        DelayLedger.globalTicks state.Ledger

    let pendingCount state = Map.count state.Pending

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
                                                NativeSequence = None
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

            if pending.NativeSequence.IsSome then
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
                                        NativeSequence = Some nativeSequence
                                }
                                state.Pending
                }
        )

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
            match pending.NativeSequence with
            | Some _ -> Error "A sent signal requires its acknowledgment."
            | None -> settle request DelayTicks.zero state
        )

    let complete request nativeSequence settlement state =
        findPending request state
        |> Result.bind (fun pending ->
            if
                pending.NativeSequence
                <> Some nativeSequence
            then
                Error "The acknowledgment does not match the pending native sequence."
            else
                let actual =
                    match settlement with
                    | SignalSettlement.Deferred -> DelayTicks.zero
                    | SignalSettlement.Measured duration -> duration

                settle request actual state
        )

    let unregister thread state =
        DelayLedger.unregister thread state.Ledger
        |> Result.map (fun ledger -> {
            state with
                Ledger = ledger
                Threads = Map.remove thread state.Threads
        })
