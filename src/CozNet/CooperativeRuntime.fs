namespace CozNet

open System
open System.Diagnostics
open System.Threading

type internal CooperativeThread = private {
    Owner: int
    Ledger: LedgerThread
}

type internal DelayRuntimeTiming = {
    Frequency: int64
    Timestamp: unit -> int64
    Wait: DelayTicks -> CancellationToken -> unit
}

[<RequireQualifiedAccess>]
module internal DelayRuntimeTiming =
    let real = {
        Frequency = Stopwatch.Frequency
        Timestamp = Stopwatch.GetTimestamp
        Wait =
            fun duration cancellation ->
                let start = Stopwatch.GetTimestamp()
                let requested = DelayTicks.value duration

                while Stopwatch.GetTimestamp()
                      - start < requested do
                    cancellation.ThrowIfCancellationRequested()

                    cancellation.WaitHandle.WaitOne(1)
                    |> ignore
    }

[<Sealed>]
type internal CooperativeRuntime(timing: DelayRuntimeTiming) =
    let gate = obj ()
    let mutable ledger = DelayLedger.create ()
    let mutable owners = Map.empty<int, LedgerThread>
    let mutable sampleStreams = Map.empty<LedgerThread, SampleCreditStream>

    let transition change =
        lock
            gate
            (fun () ->
                change ledger
                |> Result.map (fun next -> ledger <- next)
            )

    let owned thread action =
        if
            thread.Owner
            <> Environment.CurrentManagedThreadId
        then
            Error "This operation must run on the registered thread."
        else
            action ()

    member _.Register(parent: CooperativeThread option) =
        lock
            gate
            (fun () ->
                let owner = Environment.CurrentManagedThreadId

                if Map.containsKey owner owners then
                    Error "The current thread is already registered."
                else
                    DelayLedger.register
                        (parent
                         |> Option.map _.Ledger)
                        ledger
                    |> Result.map (fun (thread, next) ->
                        ledger <- next
                        owners <- Map.add owner thread owners

                        { Owner = owner; Ledger = thread }
                    )
            )

    member _.CreditSample(thread, credit) =
        transition (DelayLedger.creditSample thread.Ledger credit)

    member _.AttachSamples(thread, percent, initialCpuNanoseconds, eligibilityEpoch) =
        lock
            gate
            (fun () ->
                if Map.containsKey thread.Ledger sampleStreams then
                    Error "The thread already has a sample credit stream."
                else
                    DelayLedger.creditSample thread.Ledger DelayTicks.zero ledger
                    |> Result.bind (fun _ ->
                        SampleCreditStream.tryCreate
                            timing.Frequency
                            percent
                            initialCpuNanoseconds
                            eligibilityEpoch
                            SampleThreadState.Runnable
                    )
                    |> Result.map (fun stream ->
                        sampleStreams <- Map.add thread.Ledger stream sampleStreams
                        SampleCreditStream.identity stream
                    )
            )

    member _.CreditObservation(thread, observation) =
        lock
            gate
            (fun () ->
                match Map.tryFind thread.Ledger sampleStreams with
                | None -> Error "The thread has no sample credit stream."
                | Some stream ->
                    SampleCreditStream.observe observation stream
                    |> Result.bind (fun (credit, nextStream) ->
                        DelayLedger.creditSample thread.Ledger credit ledger
                        |> Result.map (fun nextLedger ->
                            ledger <- nextLedger
                            sampleStreams <- Map.add thread.Ledger nextStream sampleStreams
                            credit
                        )
                    )
            )

    member _.Check(thread, cancellation: CancellationToken) =
        owned
            thread
            (fun () ->
                let mutable outcome = None

                while outcome.IsNone do
                    if cancellation.IsCancellationRequested then
                        outcome <- Some(Error "The cooperative check was cancelled.")
                    else
                        let action =
                            lock
                                gate
                                (fun () ->
                                    DelayLedger.catchUp thread.Ledger ledger
                                    |> Result.map (fun (action, next) ->
                                        ledger <- next
                                        action
                                    )
                                )

                        match action with
                        | Error error -> outcome <- Some(Error error)
                        | Ok DelayAction.Continue -> outcome <- Some(Ok())
                        | Ok(DelayAction.Wait duration) ->
                            let start = timing.Timestamp()
                            let mutable waitError = None

                            try
                                timing.Wait duration cancellation
                            with error ->
                                waitError <- Some error.Message

                            let completion =
                                DelayTicks.tryCreate (
                                    timing.Timestamp()
                                    - start
                                )
                                |> Result.bind (fun actual ->
                                    transition (DelayLedger.completeDelay thread.Ledger actual)
                                )

                            match completion, waitError with
                            | Error error, _ -> outcome <- Some(Error error)
                            | Ok(), Some error -> outcome <- Some(Error error)
                            | Ok(), None -> ()

                outcome.Value
            )

    member _.BeginBlock thread =
        owned thread (fun () -> transition (DelayLedger.beginBlock thread.Ledger))

    member _.EndBlock(thread, reason) =
        owned thread (fun () -> transition (DelayLedger.endBlock thread.Ledger reason))

    member _.Unregister thread =
        owned
            thread
            (fun () ->
                lock
                    gate
                    (fun () ->
                        DelayLedger.unregister thread.Ledger ledger
                        |> Result.map (fun next ->
                            ledger <- next
                            owners <- Map.remove thread.Owner owners
                            sampleStreams <- Map.remove thread.Ledger sampleStreams
                        )
                    )
            )

    member _.GlobalDelay = lock gate (fun () -> DelayLedger.globalDelay ledger)

    member _.EffectiveDuration elapsed =
        lock gate (fun () -> DelayLedger.effectiveDuration elapsed ledger)
