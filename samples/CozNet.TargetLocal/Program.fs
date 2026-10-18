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
                   cpuNs = r.Value; wallNs = uint64 (Stopwatch.GetElapsedTime(outcome.StartedTicks, outcome.FinishedTicks).TotalNanoseconds); runDelayNs = outcome.RunDelayNs
                   startedNs = r.StartedNs; finishedNs = r.FinishedNs; finalDebt = r.FinalDebt; failure = r.Failure
                   closeOutcome = r.CloseOutcome; lifecycleError = r.LifecycleError; retireError = r.RetireError; quiescent = r.Quiescent |})
        events =
            result.Events
            |> Array.map (fun e -> [| uint64 e.Worker; e.Sequence; uint64 e.Kind; e.ObservedGlobal; e.LocalBefore; e.Amount; e.Requested; e.Ip; uint64 e.Detail |])
        eventFields = [| "worker"; "sequence"; "kind"; "observedGlobal"; "localBefore"; "amount"; "requested"; "ip"; "detail" |]
        passed = result.Passed
    |}
    if result.Passed then 0 else 1

let private phases pattern =
    match pattern with
    | "alternating" -> Array.init 10 (fun index -> if index % 2 = 0 then 2400UL, 600UL else 300UL, 600UL)
    | "selected-dominant" -> Array.create 10 (2400UL, 600UL)
    | "peer-bound" -> Array.create 10 (300UL, 600UL)
    | _ -> failwith $"Unknown phase pattern {pattern}."

let private phaseWork (barrier: TargetBarrier) (rounds: uint64 array) (work: uint64 -> uint64) () =
    for value in rounds do
        work value |> ignore
        barrier.SignalAndWait()

let private plainPhases pattern (selectedPercent: uint64) (output: string) =
    Work.selected 1UL |> ignore
    Work.peer 1UL |> ignore
    let plan = phases pattern
    let selectedRounds = plan |> Array.map (fun (a, _) -> a * selectedPercent / 100UL)
    let peerRounds = plan |> Array.map snd
    let barrier = TargetBarrier 2
    let ready = new Barrier(2)
    let started = Array.zeroCreate<int64> 2
    let finished = Array.zeroCreate<int64> 2
    let run index body =
        Thread(fun () ->
            ready.SignalAndWait()
            ready.SignalAndWait()
            started[index] <- Stopwatch.GetTimestamp()
            body ()
            finished[index] <- Stopwatch.GetTimestamp())
    let threads = [| run 0 (phaseWork barrier selectedRounds Work.selected); run 1 (phaseWork barrier peerRounds Work.peer) |]
    threads |> Array.iter _.Start()
    threads |> Array.iter _.Join()
    let elapsed = Stopwatch.GetElapsedTime(Array.min started, Array.max finished).TotalNanoseconds
    write output {| mode = "plain-phases"; pattern = pattern; selectedPercent = selectedPercent; elapsedNs = uint64 elapsed
                    phases = barrier.Phase; passed = barrier.Phase = int64 plan.Length |}
    0

let private profilePhases speedup pattern (output: string) =
    Work.selected 1UL |> ignore
    Work.peer 1UL |> ignore
    let selectedRange = range Work.selectedName
    let peerRange = range Work.peerName
    let plan = phases pattern
    let barrier = TargetBarrier 2
    let workers = [|
        { Work = phaseWork barrier (plan |> Array.map fst) Work.selected; PaymentIp = fst selectedRange }
        { Work = phaseWork barrier (plan |> Array.map snd) Work.peer; PaymentIp = fst peerRange }
    |]
    let result = TargetLocalProfiler.run speedup period [| selectedRange |] [| selectedRange; peerRange |] journalCapacity workers ignore
    let replayError, summary =
        match result.Replay with
        | Ok value -> "", Some value
        | Error message -> message, None
    let closed = result.Experiment
    let passed = result.Passed && barrier.Phase = int64 plan.Length && summary |> Option.exists (fun value -> value.DependencyWakes > 0)
    write output {|
        mode = "profile-phases"
        speedup = speedup
        pattern = pattern
        elapsedNs = result.ElapsedNs
        totalHandlerNs = result.Threads |> Array.sumBy _.Result.TotalHandlerNs
        globalNs = result.GlobalNs
        effectiveNs = result.EffectiveNs
        phases = barrier.Phase
        sequences = closed.Sequences
        ledgerFailure = closed.Failure
        replayError = replayError
        replay = summary
        threads =
            result.Threads
            |> Array.map (fun outcome ->
                let r = outcome.Result
                {| finishCode = outcome.FinishCode; error = outcome.Error; complete = r.Complete; lost = r.Lost; accepted = r.Accepted
                   maxHandlerNs = r.MaxHandlerNs; totalHandlerNs = r.TotalHandlerNs; local = r.Local; events = r.Events; finalDebt = r.FinalDebt
                   cpuNs = r.Value; wallNs = uint64 (Stopwatch.GetElapsedTime(outcome.StartedTicks, outcome.FinishedTicks).TotalNanoseconds); runDelayNs = outcome.RunDelayNs |})
        events =
            result.Events
            |> Array.map (fun e -> [| uint64 e.Worker; e.Sequence; uint64 e.Kind; e.ObservedGlobal; e.LocalBefore; e.Amount; e.Requested; e.Ip; uint64 e.Detail |])
        eventFields = [| "worker"; "sequence"; "kind"; "observedGlobal"; "localBefore"; "amount"; "requested"; "ip"; "detail" |]
        passed = passed
    |}
    if passed then 0 else 1

let private barrierAbort (output: string) =
    Work.selected 1UL |> ignore
    Work.peer 1UL |> ignore
    let selectedRange = range Work.selectedName
    let peerRange = range Work.peerName
    let barrier = TargetBarrier 2
    let failing () =
        Work.selected 300UL |> ignore
        barrier.SignalAndWait()
        failwith "worker failure"
    let workers = [|
        { Work = failing; PaymentIp = fst selectedRange }
        { Work = phaseWork barrier (Array.create 3 300UL) Work.peer; PaymentIp = fst peerRange }
    |]
    let watch = Stopwatch.StartNew()
    let result = TargetLocalProfiler.run 50u period [| selectedRange |] [| selectedRange; peerRange |] journalCapacity workers ignore
    let seconds = watch.Elapsed.TotalSeconds
    let second = TargetLocalProfiler.run 50u period [| selectedRange |] [| selectedRange; peerRange |] journalCapacity
                     [| { Work = (fun () -> Work.selected 300UL |> ignore); PaymentIp = fst selectedRange }
                        { Work = (fun () -> Work.peer 300UL |> ignore); PaymentIp = fst peerRange } |] ignore
    let failedWorker = result.Threads |> Array.exists (fun outcome -> outcome.Error.Contains "worker failure")
    let passed = not result.Passed && failedWorker && seconds < 10.0 && second.Passed
    write output {| mode = "barrier-abort"; firstRunPassed = result.Passed; failedWorker = failedWorker; seconds = seconds
                    secondRunPassed = second.Passed; passed = passed |}
    if passed then 0 else 1

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
    | [| "barrier-abort"; output |] -> barrierAbort output
    | [| "plain-phases"; pattern; selectedPercent; output |] -> plainPhases pattern (UInt64.Parse selectedPercent) output
    | [| "profile-phases"; speedup; pattern; output |] -> profilePhases (UInt32.Parse speedup) pattern output
    | [| "plain"; selectedRounds; peerRounds; output |] -> plain (UInt64.Parse selectedRounds) (UInt64.Parse peerRounds) output
    | [| "profile"; speedup; selectedRounds; peerRounds; output |] ->
        profile (UInt32.Parse speedup) (UInt64.Parse selectedRounds) (UInt64.Parse peerRounds) 0 output
    | [| "profile-gc"; speedup; selectedRounds; peerRounds; interval; output |] ->
        profile (UInt32.Parse speedup) (UInt64.Parse selectedRounds) (UInt64.Parse peerRounds) (Int32.Parse interval) output
    | _ ->
        eprintfn "usage: calibrate <out> | plain <selectedRounds> <peerRounds> <out> | profile <speedup> <selectedRounds> <peerRounds> <out> | profile-gc <speedup> <selectedRounds> <peerRounds> <intervalMs> <out>"
        64
