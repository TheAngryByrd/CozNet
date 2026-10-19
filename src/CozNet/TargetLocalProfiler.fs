namespace CozNet

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Threading

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

    [<ThreadStatic; DefaultValue>]
    static val mutable private clock: RunQueueClock

    [<ThreadStatic; DefaultValue>]
    static val mutable private waits: int64 array

    [<ThreadStatic; DefaultValue>]
    static val mutable private waitCount: int

    [<ThreadStatic; DefaultValue>]
    static val mutable private waitsDropped: int

    static member Handle = TargetCurrent.handle
    static member PaymentIp = TargetCurrent.paymentIp

    static member RunQueue() =
        if isNull TargetCurrent.clock then -1L else TargetCurrent.clock.Delay()

    static member RecordWait(before: int64, after: int64) =
        if not (isNull TargetCurrent.waits) then
            if TargetCurrent.waitCount < TargetCurrent.waits.Length then
                TargetCurrent.waits[TargetCurrent.waitCount] <- if before < 0L || after < 0L then -1L else after - before
                TargetCurrent.waitCount <- TargetCurrent.waitCount + 1
            else
                TargetCurrent.waitsDropped <- TargetCurrent.waitsDropped + 1

    static member Track(clock: RunQueueClock, capacity: int) =
        TargetCurrent.clock <- clock
        TargetCurrent.waits <- Array.zeroCreate capacity
        TargetCurrent.RecordWait(TargetCurrent.RunQueue(), TargetCurrent.RunQueue())
        TargetCurrent.waitCount <- 0
        TargetCurrent.waitsDropped <- 0

    static member TakeWaits() =
        let waits = if isNull TargetCurrent.waits then Array.empty else Array.sub TargetCurrent.waits 0 TargetCurrent.waitCount
        let dropped = TargetCurrent.waitsDropped
        TargetCurrent.clock <- null
        TargetCurrent.waits <- null
        TargetCurrent.waitCount <- 0
        TargetCurrent.waitsDropped <- 0
        waits, dropped
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
            let before = if handle <> 0n then TargetCurrent.RunQueue() else -1L

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
                    TargetCurrent.RecordWait(before, TargetCurrent.RunQueue())

            reason = TargetWakeReason.Dependency

    member this.SignalAndWait() =
        this.SignalAndWait(Timeout.InfiniteTimeSpan, CancellationToken.None) |> ignore

module internal TargetWait =
    let internal post () =
        let handle = TargetCurrent.Handle

        if handle = 0n then
            0UL
        else
            let mutable posted = 0UL
            TargetCurrent.Record(TargetLocalNative.Post(handle, TargetCurrent.PaymentIp, &posted))
            posted

    let internal acquireSpinning (spin: TimeSpan) (onSpun: unit -> unit) (semaphore: SemaphoreSlim) (timeout: TimeSpan) (cancellation: CancellationToken) (onAcquired: bool -> uint64 voption) =
        if cancellation.IsCancellationRequested || TargetCurrent.Abort.IsCancellationRequested then
            false
        elif semaphore.Wait 0 then
            onAcquired false |> ignore
            true
        else
            let handle = TargetCurrent.Handle
            let ip = TargetCurrent.PaymentIp
            let before = if handle <> 0n then TargetCurrent.RunQueue() else -1L

            if handle <> 0n then
                TargetCurrent.Record(TargetLocalNative.WaitBegin(handle, ip))

            let mutable reason = TargetWakeReason.Cancelled
            let mutable token = 0UL
            use linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, TargetCurrent.Abort)

            try
                let started = Stopwatch.GetTimestamp()
                let spinLimit =
                    if timeout = Timeout.InfiniteTimeSpan then spin
                    elif timeout < TimeSpan.Zero then TimeSpan.Zero
                    else min spin timeout

                let spun =
                    let limit = started + int64 (spinLimit.TotalSeconds * float Stopwatch.Frequency)
                    let mutable taken = false
                    let mutable waiter = SpinWait()
                    while not taken && Stopwatch.GetTimestamp() < limit && not linked.Token.IsCancellationRequested do
                        waiter.SpinOnce(-1)
                        taken <- semaphore.Wait 0
                    taken

                let acquired =
                    if spun then
                        onSpun ()
                        TargetWakeReason.Dependency
                    else
                        let remaining =
                            if spinLimit <= TimeSpan.Zero || timeout = Timeout.InfiniteTimeSpan then timeout
                            else max TimeSpan.Zero (timeout - Stopwatch.GetElapsedTime(started))

                        try
                            if semaphore.Wait(remaining, linked.Token) then TargetWakeReason.Dependency else TargetWakeReason.Timeout
                        with :? OperationCanceledException ->
                            TargetWakeReason.Cancelled

                reason <-
                    if acquired = TargetWakeReason.Dependency then
                        match onAcquired true with
                        | ValueSome value ->
                            token <- value
                            TargetWakeReason.Dependency
                        | ValueNone -> TargetWakeReason.Spurious
                    else
                        acquired
            finally
                if handle <> 0n then
                    let wakeToken = if reason = TargetWakeReason.Dependency then token else 0UL
                    TargetCurrent.Record(TargetLocalNative.WaitEnd(handle, ip, wakeToken, uint32 reason))
                    TargetCurrent.RecordWait(before, TargetCurrent.RunQueue())

            reason = TargetWakeReason.Dependency || reason = TargetWakeReason.Spurious

    let private notSpun () = ()

    let internal acquireWithSpin spin semaphore timeout cancellation onAcquired = acquireSpinning spin notSpun semaphore timeout cancellation onAcquired

    let internal acquire semaphore timeout cancellation onAcquired = acquireWithSpin TimeSpan.Zero semaphore timeout cancellation onAcquired

type TargetQueue<'T>(capacity: int, spin: TimeSpan) =
    do
        if capacity < 1 then
            raise (ArgumentOutOfRangeException(nameof capacity))
        if spin < TimeSpan.Zero || spin > TimeSpan.FromMilliseconds 1.0 then
            raise (ArgumentOutOfRangeException(nameof spin, "The spin must be from 0 to 1 ms."))

    let items = Collections.Concurrent.ConcurrentQueue<struct ('T * uint64)>()
    let available = new SemaphoreSlim(0)
    let spaces = new SemaphoreSlim(capacity)
    let gate = obj ()
    let releases = Collections.Generic.Queue<uint64>()
    let mutable initialSpaces = capacity
    let mutable spunTakes = 0L

    new(capacity: int) = TargetQueue<'T>(capacity, TimeSpan.Zero)

    member _.Capacity = capacity
    member _.Spin = spin
    member _.SpunDequeues = Interlocked.Read(&spunTakes)
    member _.Count = items.Count

    member _.Enqueue(item: 'T, timeout: TimeSpan, cancellation: CancellationToken) =
        let space waited =
            lock gate (fun () ->
                if waited && releases.Count > 0 then
                    ValueSome(releases.Dequeue())
                elif initialSpaces > 0 then
                    initialSpaces <- initialSpaces - 1
                    ValueNone
                elif releases.Count > 0 then
                    ValueSome(releases.Dequeue())
                else
                    raise (InvalidOperationException "The queue freed a space that it does not hold."))

        if TargetWait.acquire spaces timeout cancellation space then
            items.Enqueue(struct (item, TargetWait.post ()))
            available.Release() |> ignore
            true
        else
            false

    member _.Dequeue(timeout: TimeSpan, cancellation: CancellationToken) =
        let taken = ref ValueNone

        let take _ =
            match items.TryDequeue() with
            | true, struct (item, token) ->
                taken.Value <- ValueSome item
                ValueSome token
            | _ -> ValueNone

        if TargetWait.acquireSpinning spin (fun () -> Interlocked.Increment(&spunTakes) |> ignore) available timeout cancellation take then
            match taken.Value with
            | ValueSome item ->
                let token = TargetWait.post ()
                lock gate (fun () -> releases.Enqueue token)
                spaces.Release() |> ignore
                ValueSome item
            | ValueNone -> raise (InvalidOperationException "The queue released an item that it does not hold.")
        else
            ValueNone

    member this.Enqueue(item: 'T) =
        if not (this.Enqueue(item, Timeout.InfiniteTimeSpan, CancellationToken.None)) then
            raise (OperationCanceledException "The run aborted the enqueue.")

    member this.Dequeue() =
        match this.Dequeue(Timeout.InfiniteTimeSpan, CancellationToken.None) with
        | ValueSome item -> item
        | ValueNone -> raise (OperationCanceledException "The run aborted the dequeue.")

type TargetSemaphore(initialCount: int) =
    do
        if initialCount < 0 then
            raise (ArgumentOutOfRangeException(nameof initialCount))

    let permits = new SemaphoreSlim(initialCount)
    let gate = obj ()
    let releases = Collections.Generic.Queue<uint64>()
    let mutable initialPermits = initialCount

    member _.CurrentCount = permits.CurrentCount

    member internal _.StoredTokens = lock gate (fun () -> releases.Count)

    member _.Wait(timeout: TimeSpan, cancellation: CancellationToken) =
        let take waited =
            lock gate (fun () ->
                if waited && releases.Count > 0 then
                    ValueSome(releases.Dequeue())
                elif initialPermits > 0 then
                    initialPermits <- initialPermits - 1
                    ValueNone
                elif releases.Count > 0 then
                    ValueSome(releases.Dequeue())
                else
                    raise (InvalidOperationException "The semaphore gave a permit that it does not hold."))

        TargetWait.acquire permits timeout cancellation take

    member this.Wait() =
        if not (this.Wait(Timeout.InfiniteTimeSpan, CancellationToken.None)) then
            raise (OperationCanceledException "The run aborted the semaphore wait.")

    member _.Release() =
        lock gate (fun () ->
            if initialPermits + releases.Count = Int32.MaxValue then
                raise (SemaphoreFullException "The semaphore already holds Int32.MaxValue permits.")
            releases.Enqueue(TargetWait.post ()))
        permits.Release() |> ignore

type TargetLock(spin: TimeSpan) =
    do
        if spin < TimeSpan.Zero || spin > TimeSpan.FromMilliseconds 1.0 then
            raise (ArgumentOutOfRangeException(nameof spin, "The spin must be from 0 to 1 ms."))

    let gate = new SemaphoreSlim(1)
    let mutable releaseToken = 0UL
    let mutable releasedTicks = 0L
    let mutable owner = 0
    let mutable entries = 0L
    let mutable waitedEntries = 0L
    let mutable handoffTicks = 0L

    member _.IsHeld = Volatile.Read &owner <> 0
    member _.Entries = Volatile.Read &entries
    member _.WaitedEntries = Volatile.Read &waitedEntries
    member _.HandoffNs = int64 (Stopwatch.GetElapsedTime(0L, Volatile.Read &handoffTicks).TotalNanoseconds)

    member _.Enter(timeout: TimeSpan, cancellation: CancellationToken) =
        if Volatile.Read &owner = Environment.CurrentManagedThreadId then
            raise (LockRecursionException "The lock does not support recursion.")
        let mutable acquired = 0L

        let take blocked =
            if blocked then acquired <- Stopwatch.GetTimestamp()
            ValueSome(Volatile.Read &releaseToken)

        if TargetWait.acquireWithSpin spin gate timeout cancellation take then
            if acquired > 0L then
                let released = Volatile.Read &releasedTicks
                if released > 0L then
                    Volatile.Write(&handoffTicks, handoffTicks + max 0L (acquired - released))
                Volatile.Write(&waitedEntries, waitedEntries + 1L)
            Volatile.Write(&entries, entries + 1L)
            Volatile.Write(&owner, Environment.CurrentManagedThreadId)
            true
        else
            false

    member this.Enter() =
        if not (this.Enter(Timeout.InfiniteTimeSpan, CancellationToken.None)) then
            raise (OperationCanceledException "The run aborted the lock wait.")

    new() = TargetLock(TimeSpan.Zero)

    member _.Spin = spin

    member _.Exit() =
        if Interlocked.CompareExchange(&owner, 0, Environment.CurrentManagedThreadId) <> Environment.CurrentManagedThreadId then
            raise (SynchronizationLockException "The calling thread does not hold the lock.")
        Volatile.Write(&releaseToken, TargetWait.post ())
        Volatile.Write(&releasedTicks, Stopwatch.GetTimestamp())
        gate.Release() |> ignore

type TargetWorker = {
    Work: unit -> unit
    PaymentIp: uint64
}

type TargetThreadOutcome = {
    RunDelayNs: int64
    StartRunDelayNs: int64
    StartTrace: TargetStartPoint array
    WaitRunDelayNs: int64 array
    WaitRunDelaysDropped: int
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
    let internal handlerLimit = 5000000UL

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

    let private waitCapacity = 1024

    let private delayOf (clock: RunQueueClock) = if isNull clock then -1L else clock.Delay()

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
                use clock = RunQueueClock.Open()
                let trace = StartTrace clock
                trace.Record 0 |> ignore
                ready.SignalAndWait()

                if registered <> 0 then
                    abort.Cancel()
                    ready.SignalAndWait()
                    outcomes[index] <- { RunDelayNs = -1L; StartRunDelayNs = -1L; StartTrace = Array.empty; WaitRunDelayNs = Array.empty; WaitRunDelaysDropped = 0; Result = TargetThreadResult(); FinishCode = registered; Error = $"register {registered}"; StartedTicks = 0L; FinishedTicks = 0L }
                else
                    TargetCurrent.Set(handle, worker.PaymentIp, abort)
                    TargetCurrent.Track(clock, waitCapacity)
                    let started = TargetLocalNative.Start handle
                    trace.Record 0 |> ignore
                    if started <> 0 then abort.Cancel()
                    let delayBefore = trace.Record 1
                    ready.SignalAndWait()
                    let delayStarted = trace.Record 2
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
                    let delayAfter = delayOf clock
                    let waits, waitsDropped = TargetCurrent.TakeWaits()
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
                    outcomes[index] <- { RunDelayNs = (if delayBefore < 0L || delayAfter < 0L then -1L else delayAfter - delayBefore)
                                         StartRunDelayNs = (if delayBefore < 0L || delayStarted < 0L then -1L else delayStarted - delayBefore)
                                         StartTrace = trace.Points()
                                         WaitRunDelayNs = waits; WaitRunDelaysDropped = waitsDropped; Result = result; FinishCode = code; Error = error; StartedTicks = startedTicks; FinishedTicks = finishedTicks })

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
