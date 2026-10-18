namespace CozNet

open System

[<RequireQualifiedAccess>]
type TargetLedgerEventKind =
    | Sample = 0
    | Publish = 1
    | Child = 2
    | Deferred = 3
    | Failure = 4

[<Struct>]
type TargetLedgerEvent = {
    Worker: int
    Sequence: uint64
    Kind: TargetLedgerEventKind
    ObservedGlobal: uint64
    LocalBefore: uint64
    Amount: uint64
    Requested: uint64
    Ip: uint64
    Detail: uint32
}

type TargetReplayPlan = private {
    PlanSpeedup: uint32
    PlanPeriod: uint64
    PlanSelected: (uint64 * uint64) array
    PlanEligible: (uint64 * uint64) array
} with

    member this.Credit =
        this.PlanPeriod
        * uint64 this.PlanSpeedup
        / 100UL

[<RequireQualifiedAccess>]
module TargetReplayPlan =
    let private validSpans (spans: (uint64 * uint64) array) =
        spans
        |> Array.forall (fun (first, last) -> first <> 0UL && last > first)
        && spans
           |> Array.sortBy fst
           |> Array.pairwise
           |> Array.forall (fun ((_, last), (next, _)) -> next >= last)

    let tryCreate speedup period (selected: (uint64 * uint64) array) (eligible: (uint64 * uint64) array) =
        if speedup > 100u || period = 0UL || period > 1000000000UL then
            Error "The replay plan requires speedup 0 through 100 and a period from 1 ns through 1 s."
        elif selected.Length > 16 || eligible.Length > 16 then
            Error "The replay plan allows at most 16 spans in each set."
        elif not (validSpans selected) || not (validSpans eligible) || Array.isEmpty eligible then
            Error "The replay plan requires nonzero, nonoverlapping spans and at least one eligible span."
        else
            Ok {
                PlanSpeedup = speedup
                PlanPeriod = period
                PlanSelected = Array.copy selected
                PlanEligible = Array.copy eligible
            }

    let internal contains spans ip =
        spans
        |> Array.exists (fun (first, last) -> ip >= first && ip < last)

type TargetReplaySummary = {
    GlobalDelay: uint64
    Locals: uint64 array
    SelectedSamples: uint64
    Credited: uint64
    Publishes: int
    Children: int
    MeasuredPayment: uint64
    Deferred: int
    Failures: int
    MaxObservedDebt: uint64
}

[<RequireQualifiedAccess>]
module TargetLocalReplay =
    let private childLimit = 50000UL

    let replay (plan: TargetReplayPlan) (workers: int) (events: TargetLedgerEvent array) =
        let total = uint64 events.Length

        if workers <= 0 then
            Error "The replay requires at least one worker."
        elif events |> Array.exists (fun event -> event.Worker < 0 || event.Worker >= workers) then
            Error "An event names an unknown worker."
        else
            let ordered = events |> Array.sortBy _.Sequence

            let gapless =
                ordered
                |> Array.mapi (fun index event -> event.Sequence = uint64 index + 1UL)
                |> Array.forall id

            if not gapless then
                Error $"The {total} event sequences are not exactly 1 through {total}."
            else
                let publishes =
                    ordered
                    |> Array.filter (fun event -> event.Kind = TargetLedgerEventKind.Publish)
                    |> Array.sortBy _.Amount

                let published =
                    publishes
                    |> Array.map _.Amount
                    |> Set.ofArray
                    |> Set.add 0UL

                let chain =
                    publishes
                    |> Array.fold
                        (fun (valid, predecessor) event ->
                            valid
                            && event.ObservedGlobal = predecessor
                            && event.Amount > event.ObservedGlobal,
                            event.Amount)
                        (true, 0UL)
                    |> fst

                if not chain then
                    Error "The publish chain has a missing or duplicate predecessor."
                else
                    let locals = Array.zeroCreate<uint64> workers
                    let mutable failure = None

                    let reject message =
                        if failure.IsNone then
                            failure <- Some message

                    let mutable selectedSamples = 0UL
                    let mutable credited = 0UL
                    let mutable children = 0
                    let mutable measured = 0UL
                    let mutable deferred = 0
                    let mutable failures = 0
                    let mutable maxDebt = 0UL

                    for event in ordered do
                        let local = locals[event.Worker]

                        if event.LocalBefore <> local then
                            reject $"Event {event.Sequence} expects local delay {event.LocalBefore}, but the replay has {local}."

                        if not (published.Contains event.ObservedGlobal) then
                            reject $"Event {event.Sequence} observes a global delay that no publish produced."

                        if event.ObservedGlobal > event.LocalBefore then
                            maxDebt <- max maxDebt (event.ObservedGlobal - event.LocalBefore)

                        match event.Kind with
                        | TargetLedgerEventKind.Sample ->
                            let selected = TargetReplayPlan.contains plan.PlanSelected event.Ip
                            let expected = if selected then plan.Credit else 0UL

                            if event.Amount <> expected || event.Detail <> (if selected then 1u else 0u) then
                                reject $"Sample event {event.Sequence} has the wrong credit."

                            if selected then
                                selectedSamples <- selectedSamples + 1UL

                            credited <- credited + event.Amount
                            locals[event.Worker] <- local + event.Amount
                        | TargetLedgerEventKind.Publish ->
                            if event.Amount <> local then
                                reject $"Publish event {event.Sequence} does not publish the local delay."
                        | TargetLedgerEventKind.Child ->
                            if
                                event.Requested = 0UL
                                || event.Requested > childLimit
                                || not (TargetReplayPlan.contains plan.PlanEligible event.Ip)
                                || event.ObservedGlobal <= event.LocalBefore
                                || event.Requested > event.ObservedGlobal - event.LocalBefore
                            then
                                reject $"Child event {event.Sequence} breaks the payment rule."

                            if event.Detail <> 0u then
                                reject $"Child event {event.Sequence} records a failed wait."

                            children <- children + 1
                            measured <- measured + event.Amount
                            locals[event.Worker] <- local + event.Amount
                        | TargetLedgerEventKind.Deferred ->
                            let eligible = TargetReplayPlan.contains plan.PlanEligible event.Ip

                            if
                                event.ObservedGlobal <= event.LocalBefore
                                || event.Amount <> event.ObservedGlobal - event.LocalBefore
                                || (event.Detail = 0u && eligible)
                                || (event.Detail = 1u && not eligible)
                                || event.Detail > 1u
                            then
                                reject $"Deferred event {event.Sequence} does not match its debt or reason."

                            deferred <- deferred + 1
                        | TargetLedgerEventKind.Failure ->
                            failures <- failures + 1
                            reject $"Event {event.Sequence} records ledger failure {event.Detail}."
                        | _ -> reject $"Event {event.Sequence} has an unknown kind."

                    let globalDelay =
                        if publishes.Length = 0 then 0UL else publishes[publishes.Length - 1].Amount

                    match failure with
                    | Some message -> Error message
                    | None ->
                        Ok {
                            GlobalDelay = globalDelay
                            Locals = locals
                            SelectedSamples = selectedSamples
                            Credited = credited
                            Publishes = publishes.Length
                            Children = children
                            MeasuredPayment = measured
                            Deferred = deferred
                            Failures = failures
                            MaxObservedDebt = maxDebt
                        }

    let effectiveDuration (elapsed: uint64) (summary: TargetReplaySummary) =
        if elapsed <= summary.GlobalDelay then
            Error "The effective duration must be positive."
        else
            Ok(elapsed - summary.GlobalDelay)
