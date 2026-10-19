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

let private bind () =
    Work.selected 1UL |> ignore
    Work.peer 1UL |> ignore
    CodeBinding.tryCapture (File.ReadAllLines(PerfMap.currentProcess ())) [| Work.selectedName; Work.peerName |]
    |> Result.defaultWith failwith

let private boundRun binding speedup workers monitor =
    TargetLocalProfiler.runBound binding speedup period [| Work.selectedName |] [| Work.selectedName; Work.peerName |] journalCapacity workers monitor

let private maxSampleGap (events: TargetLedgerEvent array) worker =
    events
    |> Array.filter (fun e -> e.Worker = worker && e.Kind = TargetLedgerEventKind.Sample)
    |> Array.map _.Time
    |> Array.sort
    |> Array.pairwise
    |> Array.fold (fun gap (a, b) -> max gap (b - a)) 0UL

let private startTrace (outcome: TargetThreadOutcome) =
    outcome.StartTrace
    |> Array.map (fun p -> {| ticks = p.Ticks; cpuTimeNs = p.CpuTimeNs; runDelayNs = p.RunDelayNs; minorFaults = p.MinorFaults
                              majorFaults = p.MajorFaults; voluntarySwitches = p.VoluntarySwitches; involuntarySwitches = p.InvoluntarySwitches |})

let private profile speedup selectedRounds peerRounds (gcIntervalMs: int) (output: string) =
    let binding = bind ()
    let selectedRange = binding.Range Work.selectedName
    let peerRange = binding.Range Work.peerName
    let workers = [|
        { Work = (fun () -> Work.selected selectedRounds |> ignore); PaymentIp = fst selectedRange }
        { Work = (fun () -> Work.peer peerRounds |> ignore); PaymentIp = fst peerRange }
    |]
    let collections = GC.CollectionCount 2
    let forced = [| 0 |]
    let windows = Array.zeroCreate<int64> 2048
    let dropped = [| 0 |]
    let pauseBefore = GC.GetTotalPauseDuration()
    let monitor (state: TargetRunState) =
        if gcIntervalMs > 0 then
            while not state.Working && state.Running do
                Thread.SpinWait 100
            while state.Working do
                Thread.Sleep gcIntervalMs
                if state.Working then
                    let start = Stopwatch.GetTimestamp()
                    GC.Collect(2, GCCollectionMode.Forced, true)
                    let finish = Stopwatch.GetTimestamp()
                    if 2 * forced[0] < windows.Length then
                        windows[2 * forced[0]] <- start
                        windows[2 * forced[0] + 1] <- finish
                    else
                        dropped[0] <- dropped[0] + 1
                    forced[0] <- forced[0] + 1
    let result =
        boundRun binding speedup workers monitor
    let gen2 = GC.CollectionCount 2 - collections
    let pauseNs = int64 ((GC.GetTotalPauseDuration() - pauseBefore).TotalNanoseconds)
    let kept = min forced[0] (windows.Length / 2)
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
        gcWindows = Array.init kept (fun index -> [| windows[2 * index]; windows[2 * index + 1] |])
        gcWindowsDropped = dropped[0]
        gcPauseNs = pauseNs
        stopwatchFrequency = Stopwatch.Frequency
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
        bindingError = result.BindingError
        maxSampleGapNs = [| maxSampleGap result.Events 0; maxSampleGap result.Events 1 |]
        replay = summary
        threads =
            result.Threads
            |> Array.map (fun outcome ->
                let r = outcome.Result
                {| finishCode = outcome.FinishCode; error = outcome.Error; complete = r.Complete; lost = r.Lost
                   accepted = r.Accepted; closeSteps = r.CloseSteps; deliveries = r.Deliveries; dispatches = r.Dispatches
                   maxHandlerNs = r.MaxHandlerNs; totalHandlerNs = r.TotalHandlerNs; local = r.Local; events = r.Events
                   cpuNs = r.Value; wallNs = uint64 (Stopwatch.GetElapsedTime(outcome.StartedTicks, outcome.FinishedTicks).TotalNanoseconds); runDelayNs = outcome.RunDelayNs
                   waitRunDelayNs = outcome.WaitRunDelayNs; waitRunDelaysDropped = outcome.WaitRunDelaysDropped
                   startRunDelayNs = outcome.StartRunDelayNs; workStartedNs = outcome.StartedTicks; startTrace = startTrace outcome
                   startedNs = r.StartedNs; finishedNs = r.FinishedNs; finalDebt = r.FinalDebt; failure = r.Failure
                   closeOutcome = r.CloseOutcome; lifecycleError = r.LifecycleError; retireError = r.RetireError; quiescent = r.Quiescent |})
        events =
            result.Events
            |> Array.map (fun e -> [| uint64 e.Worker; e.Sequence; uint64 e.Kind; e.ObservedGlobal; e.LocalBefore; e.Amount; e.Requested; e.Ip; uint64 e.Detail; e.Time |])
        eventFields = [| "worker"; "sequence"; "kind"; "observedGlobal"; "localBefore"; "amount"; "requested"; "ip"; "detail"; "time" |]
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
    let binding = bind ()
    let selectedRange = binding.Range Work.selectedName
    let peerRange = binding.Range Work.peerName
    let plan = phases pattern
    let barrier = TargetBarrier 2
    let workers = [|
        { Work = phaseWork barrier (plan |> Array.map fst) Work.selected; PaymentIp = fst selectedRange }
        { Work = phaseWork barrier (plan |> Array.map snd) Work.peer; PaymentIp = fst peerRange }
    |]
    let result = boundRun binding speedup workers ignore
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
        bindingError = result.BindingError
        maxSampleGapNs = [| maxSampleGap result.Events 0; maxSampleGap result.Events 1 |]
        replay = summary
        threads =
            result.Threads
            |> Array.map (fun outcome ->
                let r = outcome.Result
                {| finishCode = outcome.FinishCode; error = outcome.Error; complete = r.Complete; lost = r.Lost; accepted = r.Accepted
                   maxHandlerNs = r.MaxHandlerNs; totalHandlerNs = r.TotalHandlerNs; local = r.Local; events = r.Events; finalDebt = r.FinalDebt
                   cpuNs = r.Value; wallNs = uint64 (Stopwatch.GetElapsedTime(outcome.StartedTicks, outcome.FinishedTicks).TotalNanoseconds); runDelayNs = outcome.RunDelayNs
                   waitRunDelayNs = outcome.WaitRunDelayNs; waitRunDelaysDropped = outcome.WaitRunDelaysDropped
                   startRunDelayNs = outcome.StartRunDelayNs; workStartedNs = outcome.StartedTicks; startTrace = startTrace outcome; startedNs = r.StartedNs; finishedNs = r.FinishedNs |})
        events =
            result.Events
            |> Array.map (fun e -> [| uint64 e.Worker; e.Sequence; uint64 e.Kind; e.ObservedGlobal; e.LocalBefore; e.Amount; e.Requested; e.Ip; uint64 e.Detail; e.Time |])
        eventFields = [| "worker"; "sequence"; "kind"; "observedGlobal"; "localBefore"; "amount"; "requested"; "ip"; "detail"; "time" |]
        passed = passed
    |}
    if passed then 0 else 1

let private barrierAbort (output: string) =
    let binding = bind ()
    let selectedRange = binding.Range Work.selectedName
    let peerRange = binding.Range Work.peerName
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
    let result = boundRun binding 50u workers ignore
    let seconds = watch.Elapsed.TotalSeconds
    let second = boundRun binding 50u
                     [| { Work = (fun () -> Work.selected 300UL |> ignore); PaymentIp = fst selectedRange }
                        { Work = (fun () -> Work.peer 300UL |> ignore); PaymentIp = fst peerRange } |] ignore
    let failedWorker = result.Threads |> Array.exists (fun outcome -> outcome.Error.Contains "worker failure")
    let passed = not result.Passed && failedWorker && seconds < 10.0 && second.Passed
    write output {| mode = "barrier-abort"; firstRunPassed = result.Passed; failedWorker = failedWorker; seconds = seconds
                    secondRunPassed = second.Passed; passed = passed |}
    if passed then 0 else 1

let private throughputDuration = 120

let private loop (counter: TargetProgressCounter) (work: uint64 -> uint64) rounds () =
    while not counter.Stopped do
        work rounds |> ignore
        counter.Visit()

let private profileDesign binding seed rotation progress =
    TargetCausalDesign.tryCreate binding period [| Work.selectedName; Work.peerName |] [| Work.selectedName; Work.peerName |] [| 25u; 50u; 75u |] 1 seed rotation progress
    |> Result.defaultWith (fun errors -> failwith (errors |> List.map TargetCausalDesign.describe |> String.concat " "))

let private writeProfileWith mode seed (rotation: int) selectedRounds peerRounds durationMs (lockStats: obj) (profile: TargetCausalProfileResult) (output: string) =
    let passed = profile.Experiments.Length = 7 && profile.Experiments |> Array.forall _.Passed
    let points =
        profile.Points
        |> Array.map (fun p -> {| candidate = p.Candidate; speedup = p.Speedup; prediction = p.Predictions[0] |> Option.toNullable |})
    write output {| mode = mode; seed = seed; rotation = rotation; selectedRounds = selectedRounds; peerRounds = peerRounds; durationMs = durationMs
                    warmup = profile.Warmup; experiments = profile.Experiments; points = points; lockStats = lockStats; passed = passed |}
    if passed then 0 else 1

let private writeProfile mode seed rotation selectedRounds peerRounds durationMs profile output =
    writeProfileWith mode seed rotation selectedRounds peerRounds durationMs null profile output

let private causalProfile (seed: int) (rotation: int) (output: string) =
    let binding = bind ()
    let selectedRange = binding.Range Work.selectedName
    let peerRange = binding.Range Work.peerName
    let workers (_: TargetProgressCounter) = [|
        { Work = (fun () -> Work.selected 6000UL |> ignore); PaymentIp = fst selectedRange }
        { Work = (fun () -> Work.peer 3000UL |> ignore); PaymentIp = fst peerRange }
    |]
    let profile = TargetCausalProfile.run (profileDesign binding seed rotation EndToEnd) journalCapacity workers
    writeProfile "causal-profile" seed rotation 6000 3000 0 profile output

let private throughputProfile (seed: int) (rotation: int) (output: string) =
    let binding = bind ()
    let selectedRange = binding.Range Work.selectedName
    let peerRange = binding.Range Work.peerName
    let workers (counter: TargetProgressCounter) = [|
        { Work = loop counter Work.selected 40UL; PaymentIp = fst selectedRange }
        { Work = loop counter Work.peer 20UL; PaymentIp = fst peerRange }
    |]
    let profile = TargetCausalProfile.run (profileDesign binding seed rotation (Throughput throughputDuration)) journalCapacity workers
    writeProfile "throughput-profile" seed rotation 40 20 throughputDuration profile output

let private pipelineCapacity = 4

let private produce (counter: TargetProgressCounter) (queue: TargetQueue<int>) rounds () =
    while not counter.Stopped do
        Work.selected rounds |> ignore
        queue.Enqueue 1
    queue.Enqueue 0

let private consume (counter: TargetProgressCounter) (queue: TargetQueue<int>) rounds () =
    let mutable running = true
    while running do
        if queue.Dequeue() = 0 then
            running <- false
        else
            Work.peer rounds |> ignore
            counter.Visit()

let private pipelineProfile (seed: int) (rotation: int) (output: string) =
    let binding = bind ()
    let selectedRange = binding.Range Work.selectedName
    let peerRange = binding.Range Work.peerName
    let workers (counter: TargetProgressCounter) =
        let queue = TargetQueue<int> pipelineCapacity
        [| { Work = produce counter queue 40UL; PaymentIp = fst selectedRange }
           { Work = consume counter queue 20UL; PaymentIp = fst peerRange } |]
    let profile = TargetCausalProfile.run (profileDesign binding seed rotation (Throughput throughputDuration)) journalCapacity workers
    writeProfile "pipeline-profile" seed rotation 40 20 throughputDuration profile output

let private guarded (counter: TargetProgressCounter) (gate: TargetLock) inside outside () =
    while not counter.Stopped do
        Work.peer outside |> ignore
        gate.Enter()
        try
            Work.selected inside |> ignore
        finally
            gate.Exit()
        counter.Visit()

let private lockSpin = TimeSpan.FromMilliseconds 1.0

let private lockProfileWith (mode: string) (spin: TimeSpan) (seed: int) (rotation: int) (output: string) =
    let binding = bind ()
    let selectedRange = binding.Range Work.selectedName
    let peerRange = binding.Range Work.peerName
    let locks = Collections.Generic.List<TargetLock>()
    let workers (counter: TargetProgressCounter) =
        let gate = TargetLock spin
        locks.Add gate
        [| { Work = guarded counter gate 40UL 20UL; PaymentIp = fst selectedRange }
           { Work = guarded counter gate 40UL 20UL; PaymentIp = fst peerRange } |]
    let profile = TargetCausalProfile.run (profileDesign binding seed rotation (Throughput throughputDuration)) journalCapacity workers
    let stats = locks |> Seq.map (fun l -> box {| entries = l.Entries; waitedEntries = l.WaitedEntries; handoffNs = l.HandoffNs |}) |> Seq.toArray
    writeProfileWith mode seed rotation 40 20 throughputDuration (box stats) profile output

let private plainLockWith (mode: string) (spin: TimeSpan) selectedRounds peerRounds (output: string) =
    Work.selected 1UL |> ignore
    Work.peer 1UL |> ignore
    let counter = TargetProgressCounter()
    let gate = TargetLock spin
    let ready = new Barrier(3)
    let started = Array.zeroCreate<int64> 2
    let finished = Array.zeroCreate<int64> 2
    let run index =
        Thread(fun () ->
            ready.SignalAndWait()
            started[index] <- Stopwatch.GetTimestamp()
            guarded counter gate selectedRounds peerRounds ()
            finished[index] <- Stopwatch.GetTimestamp())
    let threads = [| run 0; run 1 |]
    threads |> Array.iter _.Start()
    ready.SignalAndWait()
    Thread.Sleep throughputDuration
    counter.Stop()
    threads |> Array.iter _.Join()
    let elapsed = Stopwatch.GetElapsedTime(Array.min started, Array.max finished).TotalNanoseconds
    let visits = counter.Visits
    write output {| mode = mode; selectedRounds = selectedRounds; peerRounds = peerRounds; durationMs = throughputDuration
                    elapsedNs = uint64 elapsed; visits = visits; entries = gate.Entries; waitedEntries = gate.WaitedEntries; handoffNs = gate.HandoffNs
                    passed = visits >= 100L |}
    if visits >= 100L then 0 else 1

type private Turns() =
    let mutable finished = 0
    member val Selected = TargetSemaphore 0
    member val Peer = TargetSemaphore 0
    member _.Finish() = Volatile.Write(&finished, 1)
    member _.Finished = Volatile.Read &finished = 1

let private leadTurns (counter: TargetProgressCounter) (turns: Turns) rounds () =
    try
        while not counter.Stopped do
            Work.selected rounds |> ignore
            turns.Peer.Release()
            turns.Selected.Wait()
    finally
        turns.Finish()
        turns.Peer.Release()

let private followTurns (counter: TargetProgressCounter) (turns: Turns) rounds () =
    let mutable running = true
    while running do
        turns.Peer.Wait()
        if turns.Finished then
            running <- false
        else
            Work.peer rounds |> ignore
            counter.Visit()
            turns.Selected.Release()

let private semaphoreProfile (seed: int) (rotation: int) (output: string) =
    let binding = bind ()
    let selectedRange = binding.Range Work.selectedName
    let peerRange = binding.Range Work.peerName
    let workers (counter: TargetProgressCounter) =
        let turns = Turns()
        [| { Work = leadTurns counter turns 24UL; PaymentIp = fst selectedRange }
           { Work = followTurns counter turns 12UL; PaymentIp = fst peerRange } |]
    let profile = TargetCausalProfile.run (profileDesign binding seed rotation (Throughput throughputDuration)) journalCapacity workers
    writeProfile "semaphore-profile" seed rotation 24 12 throughputDuration profile output

let private plainSemaphore selectedRounds peerRounds (output: string) =
    Work.selected 1UL |> ignore
    Work.peer 1UL |> ignore
    let counter = TargetProgressCounter()
    let turns = Turns()
    let ready = new Barrier(3)
    let started = Array.zeroCreate<int64> 2
    let finished = Array.zeroCreate<int64> 2
    let run index (body: unit -> unit) =
        Thread(fun () ->
            ready.SignalAndWait()
            started[index] <- Stopwatch.GetTimestamp()
            body ()
            finished[index] <- Stopwatch.GetTimestamp())
    let threads = [| run 0 (leadTurns counter turns selectedRounds); run 1 (followTurns counter turns peerRounds) |]
    threads |> Array.iter _.Start()
    ready.SignalAndWait()
    Thread.Sleep throughputDuration
    counter.Stop()
    threads |> Array.iter _.Join()
    let elapsed = Stopwatch.GetElapsedTime(Array.min started, Array.max finished).TotalNanoseconds
    let visits = counter.Visits
    write output {| mode = "plain-semaphore"; selectedRounds = selectedRounds; peerRounds = peerRounds; durationMs = throughputDuration
                    elapsedNs = uint64 elapsed; visits = visits; passed = visits >= 100L |}
    if visits >= 100L then 0 else 1

let private plainPipeline selectedRounds peerRounds (output: string) =
    Work.selected 1UL |> ignore
    Work.peer 1UL |> ignore
    let counter = TargetProgressCounter()
    let queue = TargetQueue<int> pipelineCapacity
    let ready = new Barrier(3)
    let started = Array.zeroCreate<int64> 2
    let finished = Array.zeroCreate<int64> 2
    let run index (body: unit -> unit) =
        Thread(fun () ->
            ready.SignalAndWait()
            started[index] <- Stopwatch.GetTimestamp()
            body ()
            finished[index] <- Stopwatch.GetTimestamp())
    let threads = [| run 0 (produce counter queue selectedRounds); run 1 (consume counter queue peerRounds) |]
    threads |> Array.iter _.Start()
    ready.SignalAndWait()
    Thread.Sleep throughputDuration
    counter.Stop()
    threads |> Array.iter _.Join()
    let elapsed = Stopwatch.GetElapsedTime(Array.min started, Array.max finished).TotalNanoseconds
    let visits = counter.Visits
    write output {| mode = "plain-pipeline"; selectedRounds = selectedRounds; peerRounds = peerRounds; durationMs = throughputDuration
                    elapsedNs = uint64 elapsed; visits = visits; passed = visits >= 100L |}
    if visits >= 100L then 0 else 1

let private plainThroughput selectedRounds peerRounds (output: string) =
    Work.selected 1UL |> ignore
    Work.peer 1UL |> ignore
    let counter = TargetProgressCounter()
    let ready = new Barrier(3)
    let started = Array.zeroCreate<int64> 2
    let finished = Array.zeroCreate<int64> 2
    let run index (work: uint64 -> uint64) rounds =
        Thread(fun () ->
            ready.SignalAndWait()
            started[index] <- Stopwatch.GetTimestamp()
            loop counter work rounds ()
            finished[index] <- Stopwatch.GetTimestamp())
    let threads = [| run 0 Work.selected selectedRounds; run 1 Work.peer peerRounds |]
    threads |> Array.iter _.Start()
    ready.SignalAndWait()
    Thread.Sleep throughputDuration
    counter.Stop()
    threads |> Array.iter _.Join()
    let elapsed = Stopwatch.GetElapsedTime(Array.min started, Array.max finished).TotalNanoseconds
    let visits = counter.Visits
    write output {| mode = "plain-throughput"; selectedRounds = selectedRounds; peerRounds = peerRounds; durationMs = throughputDuration
                    elapsedNs = uint64 elapsed; visits = visits; passed = visits >= 100L |}
    if visits >= 100L then 0 else 1

module private ReJitProbe =
    [<System.Runtime.InteropServices.DllImport("CozNet.ReJitProbe", EntryPoint = "coz_rejit_request")>]
    extern int request(unativeint functionId)

    [<System.Runtime.InteropServices.DllImport("CozNet.ReJitProbe", EntryPoint = "coz_rejit_status")>]
    extern void status(int64& finished, int64& errors, int& last)

let private rejitCheck (output: string) =
    let binding = bind ()
    let selectedRange = binding.Range Work.selectedName
    let peerRange = binding.Range Work.peerName
    let selectedMethod = Type.GetType("CozNet.TargetLocal.Work, CozNet.TargetLocal").GetMethod("selected")
    let requested = [| Int32.MinValue |]
    let selectedWork () =
        Work.selected 1UL |> ignore
        requested[0] <- ReJitProbe.request (unativeint (selectedMethod.MethodHandle.Value))
        Work.selected 6000UL |> ignore
    let workers = [|
        { Work = selectedWork; PaymentIp = fst selectedRange }
        { Work = (fun () -> Work.peer 3000UL |> ignore); PaymentIp = fst peerRange }
    |]
    let result = boundRun binding 50u workers ignore
    let mutable finished = 0L
    let mutable errors = 0L
    let mutable last = 0
    ReJitProbe.status(&finished, &errors, &last)
    let entries = File.ReadAllLines(PerfMap.currentProcess ()) |> Array.filter (fun line -> line.Contains(Work.selectedName, StringComparison.Ordinal))
    let bindingError = result.BindingError |> Option.defaultValue ""
    let passed = requested[0] = 0 && finished >= 1L && errors = 0L && last = 0 && entries.Length > 1 && bindingError <> "" && not result.Passed
    write output {| mode = "rejit"; requestResult = requested[0]; rejitFinished = finished; rejitErrors = errors; lastStatus = last
                    selectedEntries = entries; bindingError = bindingError; runPassed = result.Passed; passed = passed |}
    if passed then 0 else 1

let private bindingCheck (output: string) =
    Work.selected 1UL |> ignore
    Work.peer 1UL |> ignore
    let configuration = CodeBinding.configurationErrors ()
    let capture = CodeBinding.tryCapture (File.ReadAllLines(PerfMap.currentProcess ())) [| Work.selectedName; Work.peerName |]
    let verified =
        match capture with
        | Ok binding ->
            Work.selected 6000UL |> ignore
            Work.peer 6000UL |> ignore
            match CodeBinding.verify (File.ReadAllLines(PerfMap.currentProcess ())) binding with
            | Ok() -> ""
            | Error message -> message
        | Error message -> message
    let tiered = Environment.GetEnvironmentVariable "DOTNET_TieredCompilation" = "1"
    let passed =
        if tiered then not configuration.IsEmpty && Result.isError capture
        else configuration.IsEmpty && Result.isOk capture && verified = ""
    write output {| mode = "binding"; tiered = tiered; configurationErrors = configuration; captureError = (match capture with Error e -> e | Ok _ -> "")
                    verifyError = verified; perfMap = File.ReadAllLines(PerfMap.currentProcess ()) |> Array.filter (fun line -> line.Contains "TargetLocal.Work::")
                    passed = passed |}
    if passed then 0 else 1

let private shutdown (output: string) =
    let binding = bind ()
    let selectedRange = binding.Range Work.selectedName
    let peerRange = binding.Range Work.peerName
    let workers = [|
        { Work = (fun () -> Work.selected 6000UL |> ignore); PaymentIp = fst selectedRange }
        { Work = (fun () -> Work.peer 6000UL |> ignore); PaymentIp = fst peerRange }
    |]
    let monitor (state: TargetRunState) =
        while not state.Working do
            Thread.SpinWait 100
        Thread.Sleep 30
        let working = state.Working
        File.WriteAllText(output + ".marker", JsonSerializer.Serialize {| working = working; running = state.Running; ticks = Stopwatch.GetTimestamp() |})
        Environment.Exit(if working then 0 else 3)
    boundRun binding 50u workers monitor |> ignore
    write output {| mode = "shutdown"; passed = false |}
    1

let private cleanup (output: string) =
    let binding = bind ()
    let selectedRange = binding.Range Work.selectedName
    let peerRange = binding.Range Work.peerName
    let workers = [|
        { Work = (fun () -> Work.selected 300UL |> ignore); PaymentIp = fst selectedRange }
        { Work = (fun () -> Work.peer 300UL |> ignore); PaymentIp = fst peerRange }
    |]
    let run monitor = boundRun binding 50u workers monitor
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
    | [| "binding"; output |] -> bindingCheck output
    | [| "rejit"; output |] -> rejitCheck output
    | [| "causal-profile"; seed; rotation; output |] -> causalProfile (Int32.Parse seed) (Int32.Parse rotation) output
    | [| "throughput-profile"; seed; rotation; output |] -> throughputProfile (Int32.Parse seed) (Int32.Parse rotation) output
    | [| "plain-throughput"; selectedRounds; peerRounds; output |] -> plainThroughput (UInt64.Parse selectedRounds) (UInt64.Parse peerRounds) output
    | [| "lock-profile"; seed; rotation; output |] -> lockProfileWith "lock-profile" TimeSpan.Zero (Int32.Parse seed) (Int32.Parse rotation) output
    | [| "plain-lock"; selectedRounds; peerRounds; output |] -> plainLockWith "plain-lock" TimeSpan.Zero (UInt64.Parse selectedRounds) (UInt64.Parse peerRounds) output
    | [| "semaphore-profile"; seed; rotation; output |] -> semaphoreProfile (Int32.Parse seed) (Int32.Parse rotation) output
    | [| "plain-semaphore"; selectedRounds; peerRounds; output |] -> plainSemaphore (UInt64.Parse selectedRounds) (UInt64.Parse peerRounds) output
    | [| "spin-lock-profile"; seed; rotation; output |] -> lockProfileWith "spin-lock-profile" lockSpin (Int32.Parse seed) (Int32.Parse rotation) output
    | [| "plain-spin-lock"; selectedRounds; peerRounds; output |] -> plainLockWith "plain-spin-lock" lockSpin (UInt64.Parse selectedRounds) (UInt64.Parse peerRounds) output
    | [| "pipeline-profile"; seed; rotation; output |] -> pipelineProfile (Int32.Parse seed) (Int32.Parse rotation) output
    | [| "plain-pipeline"; selectedRounds; peerRounds; output |] -> plainPipeline (UInt64.Parse selectedRounds) (UInt64.Parse peerRounds) output
    | [| "shutdown"; output |] -> shutdown output
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
