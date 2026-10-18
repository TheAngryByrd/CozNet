module CozNet.TargetLocal.Native

open System
open System.Runtime.InteropServices

[<Struct; StructLayout(LayoutKind.Sequential)>]
type Span = { Begin: uint64; End: uint64 }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type Event =
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
type ThreadResult =
    val mutable Generation: uint64
    val mutable Accepted: uint64
    val mutable LastCumulativeCpu: uint64
    val mutable Value: uint64
    val mutable Lost: uint64
    val mutable CloseSteps: uint64
    val mutable Deliveries: uint64
    val mutable Dispatches: uint64
    val mutable MaxHandlerNs: uint64
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
type ExperimentResult =
    val mutable Global: uint64
    val mutable Sequences: uint64
    val mutable ForeignSignals: uint64
    val mutable SigioFallbacks: uint64
    val mutable Failure: uint32
    val mutable RestoreError: int

[<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_open")>]
extern int Open(uint32 speedup, uint64 period, Span[] selected, uint32 selectedCount, Span[] eligible, uint32 eligibleCount, nativeint& experiment)

[<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_register")>]
extern int Register(nativeint experiment, uint32 capacity, nativeint& thread)

[<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_start")>]
extern int Start(nativeint thread)

[<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_finish")>]
extern int Finish(nativeint thread, uint64 paymentIp, ThreadResult& result)

[<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_events")>]
extern int Events(nativeint thread, [<In; Out>] Event[] events, uint32 capacity, uint32& count)

[<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_release")>]
extern int Release(nativeint thread)

[<DllImport("CozNet.TargetLocal", EntryPoint = "coz_target_close")>]
extern int Close(nativeint experiment, ExperimentResult& result)

let check operation code =
    if code <> 0 then
        raise (InvalidOperationException $"Native {operation} failed with error {code}.")

let layoutsMatch () =
    Marshal.SizeOf<Span>() = 16
    && Marshal.SizeOf<Event>() = 64
    && Marshal.SizeOf<ThreadResult>() = 136
    && Marshal.SizeOf<ExperimentResult>() = 40
