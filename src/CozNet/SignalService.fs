namespace CozNet

open System

type internal SignalServiceLease = private {
    Identity: Guid
    Thread: LedgerThread
    Budget: int64
}

[<RequireQualifiedAccess>]
module internal SignalServiceLease =
    let internal tryCreate thread budget =
        if
            budget
            <= 0L
            || budget > 2000000L
        then
            Error "The service lease debt exceeds its capacity bound."
        else
            Ok {
                Identity = Guid.NewGuid()
                Thread = thread
                Budget = budget
            }

    let identity lease = lease.Identity
    let thread lease = lease.Thread
    let nanoseconds lease = lease.Budget

type internal SignalServiceChild = private {
    LeaseIdentity: Guid
    Ordinal: int64
    Sequence: uint64
    Requested: int64
    Measured: int64
    Stops: bool
}

[<RequireQualifiedAccess>]
module internal SignalServiceChild =
    let tryCreate
        leaseIdentity
        ordinal
        sequence
        requested
        measured
        handler
        observed
        outcome
        eligible
        =
        if
            leaseIdentity = Guid.Empty
            || ordinal
               <= 0L
            || ordinal > 100000L
            || sequence = 0UL
        then
            Error "The service child identity is invalid."
        elif
            requested
            <= 0L
            || requested > 50000L
        then
            Error "The service child requested wait exceeds its bound."
        elif
            measured < 0L
            || handler < measured
            || handler > 5000000L
            || observed < handler
            || observed > 100000000L
        then
            Error "The service child timing exceeds its bounds."
        elif
            outcome < 1
            || outcome > 3
            || (outcome = 1
                && measured
                   <> 0L)
            || (outcome
                <> 1
                && not eligible)
        then
            Error "The service child outcome is invalid."
        else
            Ok {
                LeaseIdentity = leaseIdentity
                Ordinal = ordinal
                Sequence = sequence
                Requested = requested
                Measured = measured
                Stops =
                    outcome
                    <> 2
            }

    let internal leaseIdentity child = child.LeaseIdentity
    let internal ordinal child = child.Ordinal
    let internal sequence child = child.Sequence
    let internal requested child = child.Requested
    let internal measured child = child.Measured
    let internal stops child = child.Stops

type internal SignalServiceStopReceipt = private {
    LeaseIdentity: Guid
    Count: int64
    Sequence: uint64
    Measured: int64
}

[<RequireQualifiedAccess>]
module internal SignalServiceStopReceipt =
    let tryCreate leaseIdentity count sequence measured outstanding =
        if
            leaseIdentity = Guid.Empty
            || count < 0L
            || count > 100000L
            || measured < 0L
        then
            Error "The service stop receipt is invalid."
        elif
            outstanding
            <> 0
        then
            Error "The service owner still has an outstanding child."
        elif
            (count = 0L
             && (sequence
                 <> 0UL
                 || measured
                    <> 0L))
            || (count > 0L
                && sequence = 0UL)
        then
            Error "The service stop receipt does not identify its journal."
        else
            Ok {
                LeaseIdentity = leaseIdentity
                Count = count
                Sequence = sequence
                Measured = measured
            }

    let internal leaseIdentity receipt = receipt.LeaseIdentity
    let internal count receipt = receipt.Count
    let internal sequence receipt = receipt.Sequence
    let internal measured receipt = receipt.Measured
