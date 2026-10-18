module CozNet.TargetLocal.Program

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Threading
open CozNet

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

let private range symbol =
    PerfMap.tryRange (File.ReadAllLines(PerfMap.currentProcess ())) symbol |> Result.defaultWith failwith

let private profile speedup selectedRounds peerRounds (gcIntervalMs: int) (output: string) =
    Work.selected 1UL |> ignore
    Work.peer 1UL |> ignore
    let selectedRange = range Work.selectedName
    let peerRange = range Work.peerName
    let workers = [|
        { Work = (fun () -> Work.selected selectedRounds |> ignore); PaymentIp = fst selectedRange }
        { Work = (fun () -> Work.peer peerRounds |> ignore); PaymentIp = fst peerRange }
    |]
    let collections = GC.CollectionCount 2
    let forced = [| 0 |]
    let monitor (state: TargetRunState) =
        if gcIntervalMs > 0 then
            while not state.Working && state.Running do
                Thread.SpinWait 100
            while state.Working do
                Thread.Sleep gcIntervalMs
                if state.Working then
                    GC.Collect(2, GCCollectionMode.Forced, true)
                    forced[0] <- forced[0] + 1
    let result =
        TargetLocalProfiler.run speedup period [| selectedRange |] [| selectedRange; peerRange |] journalCapacity workers monitor
    let gen2 = GC.CollectionCount 2 - collections
    let replayError, summary =
        match result.Replay with
        | Ok value -> "", Some value
        | Error message -> message, None
    let closed = result.Experiment
    write output {|
        mode = "profile"
        speedup = speedup
        selectedRounds = selectedRounds
        peerRounds = peerRounds
        selectedRange = selectedRange
        peerRange = peerRange
        elapsedNs = result.ElapsedNs
        totalHandlerNs = result.Threads |> Array.sumBy _.Result.TotalHandlerNs
        gcIntervalMs = gcIntervalMs
        forcedCollections = forced[0]
        gen2Collections = gen2
        startedTicks = result.Threads |> Array.map _.StartedTicks
        finishedTicks = result.Threads |> Array.map _.FinishedTicks
        globalNs = result.GlobalNs
        effectiveNs = result.EffectiveNs
        sequences = closed.Sequences
        ledgerFailure = closed.Failure
        closeCode = result.CloseCode
        foreignSignals = closed.ForeignSignals
        sigioFallbacks = closed.SigioFallbacks
        replayError = replayError
        replay = summary
        threads =
            result.Threads
            |> Array.map (fun outcome ->
                let r = outcome.Result
                {| finishCode = outcome.FinishCode; error = outcome.Error; complete = r.Complete; lost = r.Lost
                   accepted = r.Accepted; closeSteps = r.CloseSteps; deliveries = r.Deliveries; dispatches = r.Dispatches
                   maxHandlerNs = r.MaxHandlerNs; totalHandlerNs = r.TotalHandlerNs; local = r.Local; events = r.Events
                   startedNs = r.StartedNs; finishedNs = r.FinishedNs; finalDebt = r.FinalDebt; failure = r.Failure
                   closeOutcome = r.CloseOutcome; lifecycleError = r.LifecycleError; retireError = r.RetireError; quiescent = r.Quiescent |})
        events =
            result.Events
            |> Array.map (fun e -> [| uint64 e.Worker; e.Sequence; uint64 e.Kind; e.ObservedGlobal; e.LocalBefore; e.Amount; e.Requested; e.Ip; uint64 e.Detail |])
        eventFields = [| "worker"; "sequence"; "kind"; "observedGlobal"; "localBefore"; "amount"; "requested"; "ip"; "detail" |]
        passed = result.Passed
    |}
    if result.Passed then 0 else 1

let private cleanup (output: string) =
    Work.selected 1UL |> ignore
    Work.peer 1UL |> ignore
    let selectedRange = range Work.selectedName
    let peerRange = range Work.peerName
    let workers = [|
        { Work = (fun () -> Work.selected 300UL |> ignore); PaymentIp = fst selectedRange }
        { Work = (fun () -> Work.peer 300UL |> ignore); PaymentIp = fst peerRange }
    |]
    let run monitor = TargetLocalProfiler.run 50u period [| selectedRange |] [| selectedRange; peerRange |] journalCapacity workers monitor
    let failed =
        try
            run (fun state ->
                while not state.Working && state.Running do
                    Thread.SpinWait 100
                failwith "monitor failure")
            |> ignore
            "no exception"
        with
        | :? AggregateException as error when error.InnerExceptions |> Seq.exists (fun inner -> inner.Message = "monitor failure") -> ""
        | error -> error.GetType().Name
    let second = run ignore
    let passed = failed = "" && second.Passed
    write output {| mode = "cleanup"; firstRunError = failed; secondRunPassed = second.Passed; secondRunGlobalNs = second.GlobalNs; passed = passed |}
    if passed then 0 else 1

[<EntryPoint>]
let main argv =
    match argv with
    | [| "calibrate"; output |] -> calibrate output
    | [| "cleanup"; output |] -> cleanup output
    | [| "plain"; selectedRounds; peerRounds; output |] -> plain (UInt64.Parse selectedRounds) (UInt64.Parse peerRounds) output
    | [| "profile"; speedup; selectedRounds; peerRounds; output |] ->
        profile (UInt32.Parse speedup) (UInt64.Parse selectedRounds) (UInt64.Parse peerRounds) 0 output
    | [| "profile-gc"; speedup; selectedRounds; peerRounds; interval; output |] ->
        profile (UInt32.Parse speedup) (UInt64.Parse selectedRounds) (UInt64.Parse peerRounds) (Int32.Parse interval) output
    | _ ->
        eprintfn "usage: calibrate <out> | plain <selectedRounds> <peerRounds> <out> | profile <speedup> <selectedRounds> <peerRounds> <out> | profile-gc <speedup> <selectedRounds> <peerRounds> <intervalMs> <out>"
        64
