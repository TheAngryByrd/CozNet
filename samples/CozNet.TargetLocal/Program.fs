module CozNet.TargetLocal.Program

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Threading
open CozNet
open CozNet.TargetLocal.Native

let private period = 1000000UL
let private journalCapacity = 1u <<< 20

let private write (path: string) value =
    File.WriteAllText(path, JsonSerializer.Serialize(value, JsonSerializerOptions(WriteIndented = false)))

let private calibrate (output: string) =
    Work.peer 1UL |> ignore
    let rounds = 2000UL
    let watch = Stopwatch.StartNew()
    Work.peer rounds |> ignore
    let nanoseconds = watch.Elapsed.TotalNanoseconds
    write output {| mode = "calibrate"; rounds = rounds; nanoseconds = nanoseconds; roundsPerMillisecond = float rounds * 1e6 / nanoseconds |}
    0

let private plain selectedRounds peerRounds (output: string) =
    Work.selected 1UL |> ignore
    Work.peer 1UL |> ignore
    let ready = new Barrier(2)
    let started = Array.zeroCreate<int64> 2
    let finished = Array.zeroCreate<int64> 2
    let run index (work: uint64 -> uint64) rounds =
        Thread(fun () ->
            ready.SignalAndWait()
            ready.SignalAndWait()
            started[index] <- Stopwatch.GetTimestamp()
            work rounds |> ignore
            finished[index] <- Stopwatch.GetTimestamp())
    let threads = [| run 0 Work.selected selectedRounds; run 1 Work.peer peerRounds |]
    threads |> Array.iter _.Start()
    threads |> Array.iter _.Join()
    let elapsed = Stopwatch.GetElapsedTime(Array.min started, Array.max finished).TotalNanoseconds
    write output {| mode = "plain"; selectedRounds = selectedRounds; peerRounds = peerRounds; elapsedNs = uint64 elapsed
                    startedTicks = started; finishedTicks = finished; passed = true |}
    0

type private WorkerOutcome = {
    StartedTicks: int64
    FinishedTicks: int64
    Result: ThreadResult
    FinishCode: int
    Events: Event array
    Error: string
}

let private profile speedup selectedRounds peerRounds (gcIntervalMs: int) (output: string) =
    if not (layoutsMatch ()) then failwith "The native result layouts differ."
    Work.selected 1UL |> ignore
    Work.peer 1UL |> ignore
    let perfMap = $"/tmp/perf-{Environment.ProcessId}.map"
    let selectedRange = Work.range perfMap Work.selectedName
    let peerRange = Work.range perfMap Work.peerName
    let selected = [| { Begin = fst selectedRange; End = snd selectedRange } |]
    let eligible = [| selected[0]; { Begin = fst peerRange; End = snd peerRange } |]
    let mutable experiment = 0n
    check "open" (Open(speedup, period, selected, 1u, eligible, 2u, &experiment))
    let ready = new Barrier(2)
    let outcomes = Array.zeroCreate<WorkerOutcome> 2
    let working = [| 0 |]
    let run index (work: uint64 -> uint64) rounds paymentIp =
        Thread(fun () ->
            let mutable thread = 0n
            let registered = Register(experiment, journalCapacity, &thread)
            ready.SignalAndWait()
            if registered <> 0 then
                ready.SignalAndWait()
                outcomes[index] <- { StartedTicks = 0L; FinishedTicks = 0L; Result = ThreadResult(); FinishCode = registered; Events = [||]; Error = $"register {registered}" }
            else
                let started = Start thread
                ready.SignalAndWait()
                let startedTicks = Stopwatch.GetTimestamp()
                Interlocked.Increment(&working[0]) |> ignore
                if started = 0 then work rounds |> ignore
                Interlocked.Decrement(&working[0]) |> ignore
                let mutable result = ThreadResult()
                let code = Finish(thread, paymentIp, &result)
                let finishedTicks = Stopwatch.GetTimestamp()
                let buffer = Array.zeroCreate<Event> (int result.Events)
                let mutable count = 0u
                let read = Events(thread, buffer, uint32 buffer.Length, &count)
                let released = Release thread
                let error =
                    [ if started <> 0 then $"start {started}"
                      if read <> 0 then $"events {read}"
                      if released <> 0 then $"release {released}" ]
                    |> String.concat ", "
                outcomes[index] <- { StartedTicks = startedTicks; FinishedTicks = finishedTicks; Result = result; FinishCode = code; Events = buffer; Error = error })
    let threads = [| run 0 Work.selected selectedRounds selected[0].Begin; run 1 Work.peer peerRounds eligible[1].Begin |]
    let collections = GC.CollectionCount 2
    threads |> Array.iter _.Start()
    let mutable forced = 0
    if gcIntervalMs > 0 then
        while Volatile.Read(&working[0]) = 0 && threads |> Array.exists _.IsAlive do
            Thread.SpinWait 100
        while Volatile.Read(&working[0]) > 0 do
            Thread.Sleep gcIntervalMs
            if Volatile.Read(&working[0]) > 0 then
                GC.Collect(2, GCCollectionMode.Forced, true)
                forced <- forced + 1
    threads |> Array.iter _.Join()
    let gen2 = GC.CollectionCount 2 - collections
    let mutable closed = ExperimentResult()
    let closeCode = Close(experiment, &closed)
    let events =
        outcomes
        |> Array.mapi (fun worker outcome ->
            outcome.Events
            |> Array.map (fun event -> {
                Worker = worker
                Sequence = event.Sequence
                Kind = enum<TargetLedgerEventKind> (int event.Kind)
                ObservedGlobal = event.ObservedGlobal
                LocalBefore = event.LocalBefore
                Amount = event.Amount
                Requested = event.Requested
                Ip = event.Ip
                Detail = event.Detail
            }))
        |> Array.concat
    let plan =
        TargetReplayPlan.tryCreate speedup period [| selectedRange |] [| selectedRange; peerRange |]
        |> Result.defaultWith failwith
    let replay = TargetLocalReplay.replay plan 2 events
    let started = outcomes |> Array.map _.StartedTicks |> Array.min
    let finished = outcomes |> Array.map _.FinishedTicks |> Array.max
    let elapsed = uint64 (Stopwatch.GetElapsedTime(started, finished).TotalNanoseconds)
    let threadsValid =
        outcomes
        |> Array.forall (fun outcome ->
            outcome.FinishCode = 0 && outcome.Error = "" && outcome.Result.Complete = 1 && outcome.Result.Lost = 0UL
            && outcome.Result.Quiescent = 1 && outcome.Result.MaxHandlerNs < 5000000UL)
    let replayError, effective, summary =
        match replay with
        | Error message -> message, 0UL, None
        | Ok summary ->
            match TargetLocalReplay.effectiveDuration elapsed summary with
            | Ok value -> "", value, Some summary
            | Error message -> message, 0UL, Some summary
    let globalMatches = summary |> Option.exists (fun value -> value.GlobalDelay = closed.Global)
    let passed =
        threadsValid && closeCode = 0 && closed.Failure = 0u && closed.ForeignSignals = 0UL && closed.SigioFallbacks = 0UL
        && replayError = "" && globalMatches && closed.Sequences = uint64 events.Length
    write output {|
        mode = "profile"
        speedup = speedup
        selectedRounds = selectedRounds
        peerRounds = peerRounds
        selectedRange = selectedRange
        peerRange = peerRange
        elapsedNs = elapsed
        gcIntervalMs = gcIntervalMs
        forcedCollections = forced
        gen2Collections = gen2
        startedTicks = outcomes |> Array.map _.StartedTicks
        finishedTicks = outcomes |> Array.map _.FinishedTicks
        globalNs = closed.Global
        effectiveNs = effective
        sequences = closed.Sequences
        ledgerFailure = closed.Failure
        closeCode = closeCode
        foreignSignals = closed.ForeignSignals
        sigioFallbacks = closed.SigioFallbacks
        replayError = replayError
        replay = summary
        threads =
            outcomes
            |> Array.map (fun outcome ->
                let r = outcome.Result
                {| finishCode = outcome.FinishCode; error = outcome.Error; complete = r.Complete; lost = r.Lost
                   accepted = r.Accepted; closeSteps = r.CloseSteps; deliveries = r.Deliveries; dispatches = r.Dispatches
                   maxHandlerNs = r.MaxHandlerNs; local = r.Local; events = r.Events; startedNs = r.StartedNs
                   finishedNs = r.FinishedNs; finalDebt = r.FinalDebt; failure = r.Failure; closeOutcome = r.CloseOutcome
                   lifecycleError = r.LifecycleError; retireError = r.RetireError; quiescent = r.Quiescent |})
        events =
            events
            |> Array.map (fun e -> [| uint64 e.Worker; e.Sequence; uint64 e.Kind; e.ObservedGlobal; e.LocalBefore; e.Amount; e.Requested; e.Ip; uint64 e.Detail |])
        eventFields = [| "worker"; "sequence"; "kind"; "observedGlobal"; "localBefore"; "amount"; "requested"; "ip"; "detail" |]
        passed = passed
    |}
    if passed then 0 else 1

[<EntryPoint>]
let main argv =
    match argv with
    | [| "calibrate"; output |] -> calibrate output
    | [| "plain"; selectedRounds; peerRounds; output |] -> plain (UInt64.Parse selectedRounds) (UInt64.Parse peerRounds) output
    | [| "profile"; speedup; selectedRounds; peerRounds; output |] ->
        profile (UInt32.Parse speedup) (UInt64.Parse selectedRounds) (UInt64.Parse peerRounds) 0 output
    | [| "profile-gc"; speedup; selectedRounds; peerRounds; interval; output |] ->
        profile (UInt32.Parse speedup) (UInt64.Parse selectedRounds) (UInt64.Parse peerRounds) (Int32.Parse interval) output
    | _ ->
        eprintfn "usage: calibrate <out> | plain <selectedRounds> <peerRounds> <out> | profile <speedup> <selectedRounds> <peerRounds> <out> | profile-gc <speedup> <selectedRounds> <peerRounds> <intervalMs> <out>"
        64
