namespace CozNet

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Threading

[<Struct; StructLayout(LayoutKind.Sequential)>]
type internal NativeSpan = { Begin: uint64; End: uint64 }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type internal NativeEvent =
    val mutable Sequence: uint64
    val mutable ObservedGlobal: uint64
    val mutable LocalBefore: uint64
    val mutable Amount: uint64
    val mutable Requested: uint64
    val mutable Time: uint64
    val mutable Ip: uint64
    val mutable Kind: uint32
    val mutable Detail: uint32

[<Struct; StructLayout(LayoutKind.Sequential)>]
type TargetThreadResult =
    val mutable Generation: uint64
    val mutable Accepted: uint64
    val mutable LastCumulativeCpu: uint64
    val mutable Value: uint64
    val mutable Lost: uint64
    val mutable CloseSteps: uint64
    val mutable Deliveries: uint64
    val mutable Dispatches: uint64
    val mutable MaxHandlerNs: uint64
    val mutable TotalHandlerNs: uint64
    val mutable Local: uint64
    val mutable Events: uint64
    val mutable StartedNs: uint64
    val mutable FinishedNs: uint64
    val mutable FinalDebt: uint64
    val mutable Complete: int
    val mutable Failure: int
    val mutable CloseOutcome: int
    val mutable LifecycleError: int
    val mutable RetireError: int
    val mutable Quiescent: int

[<Struct; StructLayout(LayoutKind.Sequential)>]
type TargetExperimentResult =
    val mutable Global: uint64
    val mutable Sequences: uint64
    val mutable ForeignSignals: uint64
    val mutable SigioFallbacks: uint64
    val mutable Failure: uint32
    val mutable RestoreError: int

module internal TargetLocalNative =
    [<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_layout")>]
    extern uint32 Layout(uint32 which)

    [<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_open")>]
    extern int Open(uint32 speedup, uint64 period, NativeSpan[] selected, uint32 selectedCount, NativeSpan[] eligible, uint32 eligibleCount, nativeint& experiment)

    [<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_register")>]
    extern int Register(nativeint experiment, uint32 capacity, nativeint& thread)

    [<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_start")>]
    extern int Start(nativeint thread)

    [<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_finish")>]
    extern int Finish(nativeint thread, uint64 paymentIp, TargetThreadResult& result)

    [<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_post")>]
    extern int Post(nativeint thread, uint64 paymentIp, uint64& token)

    [<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_wait_begin")>]
    extern int WaitBegin(nativeint thread, uint64 paymentIp)

    [<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_wait_end")>]
    extern int WaitEnd(nativeint thread, uint64 paymentIp, uint64 token, uint32 reason)

    [<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_events")>]
    extern int Events(nativeint thread, [<In; Out>] NativeEvent[] events, uint32 capacity, uint32& count)

    [<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_release")>]
    extern int Release(nativeint thread)

    [<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_close")>]
    extern int Close(nativeint experiment, TargetExperimentResult& result)

    let layoutsMatch () =
        let sizes = [| Marshal.SizeOf<NativeSpan>(); Marshal.SizeOf<NativeEvent>(); Marshal.SizeOf<TargetThreadResult>(); Marshal.SizeOf<TargetExperimentResult>() |]
        sizes = [| 16; 64; 144; 40 |]
        && (try
                sizes |> Array.mapi (fun index size -> Layout(uint32 index) = uint32 size) |> Array.forall id
            with :? EntryPointNotFoundException ->
                false)

[<RequireQualifiedAccess>]
module PerfMap =
    let private hex (text: string) =
        let digits = if text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) then text.Substring 2 else text
        UInt64.TryParse(digits, Globalization.NumberStyles.AllowHexSpecifier, Globalization.CultureInfo.InvariantCulture)

    let private parse (line: string) =
        let parts = line.Split(' ', 3)

        if parts.Length = 3 then
            match hex parts[0], hex parts[1] with
            | (true, start), (true, size) when size > 0UL && start <= UInt64.MaxValue - size -> Some(start, start + size, parts[2])
            | _ -> None
        else
            None

    let tryRange (lines: string seq) (symbol: string) =
        let matches =
            lines
            |> Seq.choose parse
            |> Seq.filter (fun (_, _, name) -> name.Contains(symbol, StringComparison.Ordinal) && name.Contains("[Optimized]", StringComparison.Ordinal))
            |> Seq.toArray

        match matches with
        | [| first, last, _ |] -> Ok(first, last)
        | _ -> Error $"The perf map must contain one optimized range for {symbol}, but it contains {matches.Length}."

    let currentProcess () =
        $"/tmp/perf-{Environment.ProcessId}.map"

    let internal entries (lines: string seq) (symbol: string) =
        lines
        |> Seq.choose parse
        |> Seq.filter (fun (_, _, name) -> name.Contains(symbol, StringComparison.Ordinal))
        |> Seq.toArray

type CodeBinding = private {
    BoundSymbols: string array
    BoundRanges: (uint64 * uint64) array
} with

    member this.Symbols = Array.copy this.BoundSymbols
    member this.Ranges = Array.copy this.BoundRanges

    member this.Range(symbol: string) =
        match Array.tryFindIndex ((=) symbol) this.BoundSymbols with
        | Some index -> this.BoundRanges[index]
        | None -> raise (ArgumentException $"The binding has no symbol {symbol}.")

[<RequireQualifiedAccess>]
module CodeBinding =
    let private setting (read: string -> string) (name: string) =
        match read $"DOTNET_{name}" with
        | null -> read $"COMPlus_{name}"
        | value -> value

    let internal configurationErrorsWith (read: string -> string) (tieringSwitch: bool option) =
        [
            match setting read "TieredCompilation", tieringSwitch with
            | "0", _ -> ()
            | null, Some false -> ()
            | null, _ -> "Tiered compilation must be off. Set DOTNET_TieredCompilation=0 or System.Runtime.TieredCompilation=false."
            | value, _ -> $"Tiered compilation must be off, but DOTNET_TieredCompilation is {value}."

            if setting read "ReadyToRun" <> "0" then
                "ReadyToRun must be off. Set DOTNET_ReadyToRun=0."

            match setting read "PerfMapEnabled" with
            | "1"
            | "3" -> ()
            | null -> "The perf map must be on. Set DOTNET_PerfMapEnabled=3."
            | value -> $"The perf map must be on, but DOTNET_PerfMapEnabled is {value}. Set DOTNET_PerfMapEnabled=3."
        ]

    let configurationErrors () =
        let switch =
            match AppContext.TryGetSwitch "System.Runtime.TieredCompilation" with
            | true, value -> Some value
            | false, _ -> None

        configurationErrorsWith Environment.GetEnvironmentVariable switch

    let private bind (lines: string array) (symbol: string) =
        match PerfMap.entries lines symbol with
        | [| first, last, name |] when name.EndsWith("[Optimized]", StringComparison.Ordinal) -> Ok(first, last)
        | [| _, _, name |] -> Error $"{symbol} has one perf-map entry, but its tier is not [Optimized]: {name}"
        | found -> Error $"{symbol} must have exactly one perf-map entry, but it has {found.Length}."

    let tryCapture (lines: string seq) (symbols: string array) =
        let lines = Seq.toArray lines

        if symbols.Length = 0 || symbols |> Array.distinct |> Array.length <> symbols.Length then
            Error "A binding requires distinct symbols."
        else
            let results = symbols |> Array.map (bind lines)

            match results |> Array.choose (function Error message -> Some message | Ok _ -> None) with
            | [||] -> Ok { BoundSymbols = Array.copy symbols; BoundRanges = results |> Array.map (function Ok range -> range | Error _ -> 0UL, 0UL) }
            | errors -> Error(String.Join(" ", errors))

    let verify (lines: string seq) (binding: CodeBinding) =
        match tryCapture lines binding.BoundSymbols with
        | Error message -> Error message
        | Ok current when current.BoundRanges <> binding.BoundRanges -> Error "A bound method moved to another native range during the run."
        | Ok _ -> Ok()

type internal TargetCurrent() =
    [<ThreadStatic; DefaultValue>]
    static val mutable private handle: nativeint

    [<ThreadStatic; DefaultValue>]
    static val mutable private paymentIp: uint64

    [<ThreadStatic; DefaultValue>]
    static val mutable private error: int

    [<ThreadStatic; DefaultValue>]
    static val mutable private abort: CancellationToken

    [<ThreadStatic; DefaultValue>]
    static val mutable private source: CancellationTokenSource

    static member Handle = TargetCurrent.handle
    static member PaymentIp = TargetCurrent.paymentIp
    static member Error = TargetCurrent.error
    static member Abort = TargetCurrent.abort

    static member Set(value, ip, abort: CancellationToken) =
        TargetCurrent.handle <- value
        TargetCurrent.paymentIp <- ip
        TargetCurrent.error <- 0
        TargetCurrent.abort <- abort
        TargetCurrent.source <- null

    static member Set(value, ip, source: CancellationTokenSource) =
        TargetCurrent.Set(value, ip, source.Token)
        TargetCurrent.source <- source

    static member Record code =
        if code <> 0 then
            if TargetCurrent.error = 0 then
                TargetCurrent.error <- code

            if not (isNull TargetCurrent.source) then
                TargetCurrent.source.Cancel()

[<RequireQualifiedAccess>]
type TargetWakeReason =
    | Dependency = 0u
    | Timeout = 1u
    | Cancelled = 2u
    | Spurious = 3u

type internal TargetBarrierPhase(number: int64) =
    let arrived = [| 0 |]
    member _.Arrive() = Interlocked.Increment(&arrived[0])
    member val Token = 0UL with get, set
    member val Done = new ManualResetEventSlim(false)
    member _.Number = number

type TargetBarrier(participants: int) =
    do
        if participants < 1 then
            raise (ArgumentOutOfRangeException(nameof participants))

    let mutable current = TargetBarrierPhase 0L

    member _.Phase = (Volatile.Read &current).Number

    member _.SignalAndWait(timeout: TimeSpan, cancellation: CancellationToken) =
        let handle = TargetCurrent.Handle
        let ip = TargetCurrent.PaymentIp
        let phase = Volatile.Read &current
        let arrived = phase.Arrive()

        if arrived > participants then
            raise (InvalidOperationException "More workers arrived than the barrier has participants.")

        if arrived = participants then
            if handle <> 0n then
                let mutable posted = 0UL
                TargetCurrent.Record(TargetLocalNative.Post(handle, ip, &posted))
                phase.Token <- posted

            Volatile.Write(&current, TargetBarrierPhase(phase.Number + 1L))
            phase.Done.Set()
            true
        else
            if handle <> 0n then
                TargetCurrent.Record(TargetLocalNative.WaitBegin(handle, ip))

            let mutable reason = TargetWakeReason.Cancelled
            use linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, TargetCurrent.Abort)

            try
                reason <-
                    try
                        if phase.Done.Wait(timeout, linked.Token) then TargetWakeReason.Dependency else TargetWakeReason.Timeout
                    with :? OperationCanceledException ->
                        TargetWakeReason.Cancelled
            finally
                if handle <> 0n then
                    let token = if reason = TargetWakeReason.Dependency then phase.Token else 0UL
                    TargetCurrent.Record(TargetLocalNative.WaitEnd(handle, ip, token, uint32 reason))

            reason = TargetWakeReason.Dependency

    member this.SignalAndWait() =
        this.SignalAndWait(Timeout.InfiniteTimeSpan, CancellationToken.None) |> ignore

type TargetWorker = {
    Work: unit -> unit
    PaymentIp: uint64
}

type TargetThreadOutcome = {
    RunDelayNs: int64
    Result: TargetThreadResult
    FinishCode: int
    Error: string
    StartedTicks: int64
    FinishedTicks: int64
}

type TargetProfileResult = {
    ElapsedNs: uint64
    GlobalNs: uint64
    EffectiveNs: uint64
    Experiment: TargetExperimentResult
    CloseCode: int
    Threads: TargetThreadOutcome array
    Events: TargetLedgerEvent array
    Replay: Result<TargetReplaySummary, string>
    BindingError: string option
    Passed: bool
}

type TargetRunState internal (working: int array, threads: Thread array) =
    member _.Working = Volatile.Read(&working[0]) > 0
    member _.Running = threads |> Array.exists _.IsAlive

[<RequireQualifiedAccess>]
module TargetLocalProfiler =
    let private handlerLimit = 5000000UL

    let isSupported () =
        OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture = Architecture.X64

    let private check operation code =
        if code <> 0 then
            raise (InvalidOperationException $"Native {operation} failed with error {code}.")

    let private toEvent worker (event: NativeEvent) = {
        Worker = worker
        Sequence = event.Sequence
        Kind = enum<TargetLedgerEventKind> (int event.Kind)
        ObservedGlobal = event.ObservedGlobal
        LocalBefore = event.LocalBefore
        Amount = event.Amount
        Requested = event.Requested
        Ip = event.Ip
        Detail = event.Detail
        Time = event.Time
    }

    let private span (first, last) = { Begin = first; End = last }

    let private runDelay () =
        try
            let fields = File.ReadAllText("/proc/thread-self/schedstat").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            if fields.Length >= 2 then Int64.Parse fields[1] else -1L
        with _ ->
            -1L

    let run (speedup: uint32) (period: uint64) (selected: (uint64 * uint64) array) (eligible: (uint64 * uint64) array)
        (journalCapacity: uint32) (workers: TargetWorker array) (monitor: TargetRunState -> unit) =
        if not (isSupported ()) then
            raise (PlatformNotSupportedException "The target-local profiler requires Linux x64.")

        if not (TargetLocalNative.layoutsMatch ()) then
            raise (InvalidOperationException "The native target-local layouts differ.")

        let plan = TargetReplayPlan.tryCreate speedup period selected eligible |> Result.defaultWith (fun message -> raise (ArgumentException message))

        if workers.Length = 0 || workers.Length > 64 then
            raise (ArgumentException "The profiler requires 1 to 64 workers.")

        if workers |> Array.exists (fun worker -> not (TargetReplayPlan.contains plan.PlanEligible worker.PaymentIp)) then
            raise (ArgumentException "Each worker payment address must be inside an eligible span.")

        let mutable experiment = 0n
        let nativeSelected = selected |> Array.map span
        let nativeEligible = eligible |> Array.map span
        let ready = new Barrier(workers.Length)
        let outcomes = Array.zeroCreate<TargetThreadOutcome> workers.Length
        let journals = Array.create workers.Length Array.empty<TargetLedgerEvent>
        let working = [| 0 |]
        use abort = new CancellationTokenSource()

        let thread index (worker: TargetWorker) =
            Thread(fun () ->
                let mutable handle = 0n
                let registered = TargetLocalNative.Register(experiment, journalCapacity, &handle)
                runDelay () |> ignore
                ready.SignalAndWait()

                if registered <> 0 then
                    abort.Cancel()
                    ready.SignalAndWait()
                    outcomes[index] <- { RunDelayNs = -1L; Result = TargetThreadResult(); FinishCode = registered; Error = $"register {registered}"; StartedTicks = 0L; FinishedTicks = 0L }
                else
                    let started = TargetLocalNative.Start handle
                    TargetCurrent.Set(handle, worker.PaymentIp, abort)
                    if started <> 0 then abort.Cancel()
                    let delayBefore = runDelay ()
                    ready.SignalAndWait()
                    let startedTicks = Stopwatch.GetTimestamp()
                    let workError =
                        if started <> 0 then
                            ""
                        else
                            Interlocked.Increment(&working[0]) |> ignore
                            try
                                try
                                    worker.Work()
                                    ""
                                with error ->
                                    abort.Cancel()
                                    $"work {error}"
                            finally
                                Interlocked.Decrement(&working[0]) |> ignore
                    let barrierError = TargetCurrent.Error
                    TargetCurrent.Set(0n, 0UL, CancellationToken.None)
                    if barrierError <> 0 then abort.Cancel()
                    let mutable result = TargetThreadResult()
                    let code = TargetLocalNative.Finish(handle, worker.PaymentIp, &result)
                    let finishedTicks = Stopwatch.GetTimestamp()
                    let delayAfter = runDelay ()
                    let buffer = Array.zeroCreate<NativeEvent> (int result.Events)
                    let mutable count = 0u
                    let read = TargetLocalNative.Events(handle, buffer, uint32 buffer.Length, &count)
                    let released = TargetLocalNative.Release handle
                    journals[index] <- buffer |> Array.map (toEvent index)
                    let error =
                        [ if started <> 0 then $"start {started}"
                          if workError <> "" then workError
                          if barrierError <> 0 then $"barrier {barrierError}"
                          if read <> 0 then $"events {read}"
                          if released <> 0 then $"release {released}" ]
                        |> String.concat ", "
                    outcomes[index] <- { RunDelayNs = (if delayBefore < 0L || delayAfter < 0L then -1L else delayAfter - delayBefore); Result = result; FinishCode = code; Error = error; StartedTicks = startedTicks; FinishedTicks = finishedTicks })

        let threads = workers |> Array.mapi thread
        threads |> Array.iter (fun worker -> worker.IsBackground <- true)
        check "open" (TargetLocalNative.Open(speedup, period, nativeSelected, uint32 nativeSelected.Length, nativeEligible, uint32 nativeEligible.Length, &experiment))
        let mutable started = 0
        let failures = Collections.Generic.List<exn>()
        let mutable interrupted = false

        try
            try
                for worker in threads do
                    worker.Start()
                    started <- started + 1
                monitor (TargetRunState(working, threads[.. started - 1]))
            with error ->
                abort.Cancel()
                failures.Add error
        finally
            if started < threads.Length then
                try
                    ready.RemoveParticipants(threads.Length - started) |> ignore
                with error ->
                    failures.Add error
            for index in 0 .. started - 1 do
                let mutable joined = false
                while not joined do
                    try
                        threads[index].Join()
                        joined <- true
                    with :? ThreadInterruptedException ->
                        interrupted <- true

        let mutable closed = TargetExperimentResult()
        let closeCode = TargetLocalNative.Close(experiment, &closed)

        if interrupted then
            failures.Add(ThreadInterruptedException "The calling thread was interrupted while it joined the workers.")

        if failures.Count > 0 then
            raise (AggregateException("The target-local run failed before its threads finished.", failures))

        let events = Array.concat journals
        let replay = TargetLocalReplay.replay plan workers.Length events
        let started = outcomes |> Array.map _.StartedTicks |> Array.min
        let finished = outcomes |> Array.map _.FinishedTicks |> Array.max
        let elapsed = if started = 0L then 0UL else uint64 (Stopwatch.GetElapsedTime(started, finished).TotalNanoseconds)
        let effective =
            replay
            |> Result.bind (TargetLocalReplay.effectiveDuration elapsed)
            |> Result.defaultValue 0UL
        let threadsValid =
            outcomes
            |> Array.forall (fun o ->
                o.FinishCode = 0 && o.Error = "" && o.Result.Complete = 1 && o.Result.Lost = 0UL
                && o.Result.Quiescent = 1 && o.Result.MaxHandlerNs < handlerLimit)
        let replayMatches =
            match replay with
            | Ok summary -> summary.GlobalDelay = closed.Global && closed.Sequences = uint64 events.Length && effective > 0UL
            | Error _ -> false
        {
            ElapsedNs = elapsed
            GlobalNs = closed.Global
            EffectiveNs = effective
            Experiment = closed
            CloseCode = closeCode
            Threads = outcomes
            Events = events
            Replay = replay
            BindingError = None
            Passed =
                threadsValid && closeCode = 0 && closed.Failure = 0u && closed.ForeignSignals = 0UL
                && closed.SigioFallbacks = 0UL && replayMatches
        }

    let internal checkedRun (configuration: string list) (readMap: unit -> string array) (binding: CodeBinding)
        (selected: string array) (eligible: string array) (runRanges: (uint64 * uint64) array -> (uint64 * uint64) array -> 'result) =
        let refused message = raise (InvalidOperationException message)

        match configuration with
        | [] -> ()
        | errors -> refused (String.Join(" ", errors))

        match CodeBinding.verify (readMap ()) binding with
        | Error message -> refused $"The code binding is not valid before the run. {message}"
        | Ok() -> ()

        let result = runRanges (selected |> Array.map binding.Range) (eligible |> Array.map binding.Range)

        match CodeBinding.verify (readMap ()) binding with
        | Ok() -> result, None
        | Error message -> result, Some message

    let runBound (binding: CodeBinding) (speedup: uint32) (period: uint64) (selected: string array) (eligible: string array)
        (journalCapacity: uint32) (workers: TargetWorker array) (monitor: TargetRunState -> unit) =
        let readMap () = File.ReadAllLines(PerfMap.currentProcess ())

        match checkedRun (CodeBinding.configurationErrors ()) readMap binding selected eligible (fun selectedRanges eligibleRanges ->
            run speedup period selectedRanges eligibleRanges journalCapacity workers monitor) with
        | result, None -> result
        | result, Some message -> { result with BindingError = Some message; Passed = false }
