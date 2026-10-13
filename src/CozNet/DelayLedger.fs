namespace CozNet

open System

[<StructuralEquality; StructuralComparison>]
type internal DelayTicks = private DelayTicks of int64

[<RequireQualifiedAccess>]
module internal DelayTicks =
    let tryCreate ticks =
        if ticks < 0L then
            Error "Delay ticks cannot be negative."
        else
            Ok(DelayTicks ticks)

    let value (DelayTicks ticks) = ticks
    let zero = DelayTicks 0L

[<StructuralEquality; StructuralComparison>]
type internal LedgerThread = private LedgerThread of Guid * int64

[<RequireQualifiedAccess>]
type internal WakeReason =
    | Dependency
    | TimeoutOrSpurious

[<RequireQualifiedAccess>]
type internal DelayAction =
    | Continue
    | Wait of DelayTicks

type private ThreadDelay =
    | Runnable of local: int64
    | Blocked of local: int64 * globalAtBlock: int64
    | Delaying of local: int64

type internal DelayLedger = private {
    Identity: Guid
    Global: int64
    NextThread: int64
    Threads: Map<LedgerThread, ThreadDelay>
}

[<RequireQualifiedAccess>]
module internal DelayLedger =
    let create () = {
        Identity = Guid.NewGuid()
        Global = 0L
        NextThread = 0L
        Threads = Map.empty
    }

    let globalDelay ledger = DelayTicks ledger.Global

    let private add left right =
        if
            right > Int64.MaxValue
                    - left
        then
            Error "Delay accounting overflow."
        else
            Ok(
                left
                + right
            )

    let private find thread ledger =
        match Map.tryFind thread ledger.Threads with
        | Some state -> Ok state
        | None -> Error "The thread is not registered in this ledger."

    let private update thread state ledger = {
        ledger with
            Threads = Map.add thread state ledger.Threads
    }

    let private withRunnable thread ledger operation =
        find thread ledger
        |> Result.bind (
            function
            | Runnable local -> operation local
            | Blocked _ -> Error "The thread is blocked."
            | Delaying _ -> Error "The thread is applying a delay."
        )

    let register parent ledger =
        let inherited =
            match parent with
            | None -> Ok ledger.Global
            | Some thread -> withRunnable thread ledger Ok

        inherited
        |> Result.bind (fun local ->
            add ledger.NextThread 1L
            |> Result.map (fun next ->
                let thread = LedgerThread(ledger.Identity, next)

                thread,
                {
                    ledger with
                        NextThread = next
                        Threads = Map.add thread (Runnable local) ledger.Threads
                }
            )
        )

    let creditSample thread credit ledger =
        withRunnable
            thread
            ledger
            (fun local ->
                add local (DelayTicks.value credit)
                |> Result.map (fun credited -> update thread (Runnable credited) ledger)
            )

    let catchUp thread ledger =
        withRunnable
            thread
            ledger
            (fun local ->
                if local < ledger.Global then
                    Ok(
                        DelayAction.Wait(
                            DelayTicks(
                                ledger.Global
                                - local
                            )
                        ),
                        update thread (Delaying local) ledger
                    )
                else
                    Ok(DelayAction.Continue, { ledger with Global = local })
            )

    let completeDelay thread actual ledger =
        find thread ledger
        |> Result.bind (
            function
            | Delaying local ->
                add local (DelayTicks.value actual)
                |> Result.map (fun completed -> update thread (Runnable completed) ledger)
            | _ -> Error "The thread has no delay in progress."
        )

    let beginBlock thread ledger =
        withRunnable
            thread
            ledger
            (fun local -> Ok(update thread (Blocked(local, ledger.Global)) ledger))

    let endBlock thread reason ledger =
        find thread ledger
        |> Result.bind (
            function
            | Blocked(local, globalAtBlock) ->
                let skipped =
                    match reason with
                    | WakeReason.Dependency ->
                        ledger.Global
                        - globalAtBlock
                    | WakeReason.TimeoutOrSpurious -> 0L

                add local skipped
                |> Result.map (fun resumed -> update thread (Runnable resumed) ledger)
            | _ -> Error "The thread has no blocked interval."
        )

    let unregister thread ledger =
        withRunnable
            thread
            ledger
            (fun local ->
                if
                    local
                    <> ledger.Global
                then
                    Error "Settle the thread delay before exit."
                else
                    Ok {
                        ledger with
                            Threads = Map.remove thread ledger.Threads
                    }
            )

    let effectiveDuration elapsed ledger =
        let measured = DelayTicks.value elapsed

        if
            measured
            <= ledger.Global
        then
            Error "The effective experiment duration must be positive."
        else
            Ok(
                DelayTicks(
                    measured
                    - ledger.Global
                )
            )
