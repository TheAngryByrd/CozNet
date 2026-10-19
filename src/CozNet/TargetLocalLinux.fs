namespace CozNet

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices

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

[<AllowNullLiteral>]
type internal RunQueueClock private (handle: Microsoft.Win32.SafeHandles.SafeFileHandle) =
    let buffer = Array.zeroCreate<byte> 128

    static member internal Parse(bytes: byte array, count: int) =
        let mutable field = 0
        let mutable index = 0
        let mutable runTime = -1L
        let mutable delay = -1L

        while field < 2 && index < count do
            while index < count && (bytes[index] = ' 'B || bytes[index] = '\n'B) do
                index <- index + 1

            let start = index
            let mutable value = 0L

            while index < count && bytes[index] >= '0'B && bytes[index] <= '9'B do
                value <- value * 10L + int64 (bytes[index] - '0'B)
                index <- index + 1

            if index > start then
                if field = 0 then runTime <- value else delay <- value
                field <- field + 1
            else
                index <- count

        struct (runTime, delay)

    static member Open() =
        try
            new RunQueueClock(File.OpenHandle "/proc/thread-self/schedstat")
        with _ ->
            null

    member _.Read() =
        try
            RunQueueClock.Parse(buffer, RandomAccess.Read(handle, Span<byte> buffer, 0L))
        with _ ->
            struct (-1L, -1L)

    member this.Delay() =
        let struct (_, delay) = this.Read()
        delay

    interface IDisposable with
        member _.Dispose() = handle.Dispose()

module internal LinuxThreadUsage =
    [<DllImport("libc", EntryPoint = "getrusage")>]
    extern int getrusage(int who, int64[] usage)

    [<DllImport("libc", EntryPoint = "clock_gettime")>]
    extern int clock_gettime(int clock, int64[] time)

type TargetStartPoint = {
    Ticks: int64
    CpuTimeNs: int64
    RunDelayNs: int64
    MinorFaults: int64
    MajorFaults: int64
    VoluntarySwitches: int64
    InvoluntarySwitches: int64
}

type internal StartTrace(clock: RunQueueClock) =
    static let fields = 7
    // struct rusage on Linux x64: ru_minflt, ru_majflt, ru_nvcsw, and ru_nivcsw are longs 8, 9, 16, and 17.
    let usage = Array.zeroCreate<int64> 18
    let time = Array.zeroCreate<int64> 2
    let values = Array.create (3 * fields) -1L

    member _.Record(index: int) =
        let offset = index * fields
        values[offset] <- Stopwatch.GetTimestamp()
        // CLOCK_THREAD_CPUTIME_ID is 3. Schedstat field 1 lags for a running thread, so it is not used here.
        let cpu = try LinuxThreadUsage.clock_gettime(3, time) with _ -> -1
        values[offset + 1] <- (if cpu = 0 then time[0] * 1_000_000_000L + time[1] else -1L)
        let delay = if isNull clock then -1L else clock.Delay()
        values[offset + 2] <- delay
        let read = try LinuxThreadUsage.getrusage(1, usage) with _ -> -1
        values[offset + 3] <- (if read = 0 then usage[8] else -1L)
        values[offset + 4] <- (if read = 0 then usage[9] else -1L)
        values[offset + 5] <- (if read = 0 then usage[16] else -1L)
        values[offset + 6] <- (if read = 0 then usage[17] else -1L)
        delay

    member _.Points() =
        Array.init 3 (fun index ->
            let offset = index * fields
            { Ticks = values[offset]; CpuTimeNs = values[offset + 1]; RunDelayNs = values[offset + 2]; MinorFaults = values[offset + 3]
              MajorFaults = values[offset + 4]; VoluntarySwitches = values[offset + 5]; InvoluntarySwitches = values[offset + 6] })
