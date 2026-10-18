namespace CozNet.Tests

open CozNet
open Expecto

module TargetLocalReplayTests =
    let private selectedIp = 0x1800UL
    let private peerIp = 0x2800UL

    let private plan =
        TargetReplayPlan.tryCreate 50u 100000UL [| 0x1000UL, 0x2000UL |] [| 0x1000UL, 0x3000UL |]
        |> Result.defaultWith failwith

    let private event worker sequence kind observed before amount requested ip detail = {
        Worker = worker
        Sequence = sequence
        Kind = kind
        ObservedGlobal = observed
        LocalBefore = before
        Amount = amount
        Requested = requested
        Ip = ip
        Detail = detail
        Time = 0UL
    }

    let private journal = [|
        event 0 1UL TargetLedgerEventKind.Sample 0UL 0UL 50000UL 0UL selectedIp 1u
        event 0 2UL TargetLedgerEventKind.Publish 0UL 50000UL 50000UL 0UL selectedIp 0u
        event 1 3UL TargetLedgerEventKind.Sample 50000UL 0UL 0UL 0UL peerIp 0u
        event 1 4UL TargetLedgerEventKind.Child 50000UL 0UL 30000UL 30000UL peerIp 0u
        event 1 5UL TargetLedgerEventKind.Child 50000UL 30000UL 20700UL 20000UL peerIp 0u
        event 1 6UL TargetLedgerEventKind.Publish 50000UL 50700UL 50700UL 0UL peerIp 0u
    |]

    let private barrier = [|
        event 1 1UL TargetLedgerEventKind.Block 0UL 0UL 0UL 0UL peerIp 0u
        event 0 2UL TargetLedgerEventKind.Sample 0UL 0UL 50000UL 0UL selectedIp 1u
        event 0 3UL TargetLedgerEventKind.Publish 0UL 50000UL 50000UL 0UL selectedIp 0u
        event 0 4UL TargetLedgerEventKind.Post 50000UL 50000UL 50000UL 0UL selectedIp 0u
        event 0 5UL TargetLedgerEventKind.Sample 50000UL 50000UL 50000UL 0UL selectedIp 1u
        event 0 6UL TargetLedgerEventKind.Publish 50000UL 100000UL 100000UL 0UL selectedIp 0u
        event 1 7UL TargetLedgerEventKind.Wake 100000UL 0UL 50000UL 50000UL peerIp 0u
        event 1 8UL TargetLedgerEventKind.Child 100000UL 50000UL 50000UL 50000UL peerIp 0u
    |]

    let private replayed events = TargetLocalReplay.replay plan 2 events

    let private rejected events (reason: string) message =
        match replayed events with
        | Ok _ -> failtest message
        | Error actual -> Expect.stringContains actual reason message

    let private replace index change =
        journal
        |> Array.mapi (fun position value -> if position = index then change value else value)

    [<Tests>]
    let tests =
        testList "target-local replay" [
            test "plans reject invalid settings" {
                Expect.isError (TargetReplayPlan.tryCreate 101u 1000000UL [||] [| 1UL, 2UL |]) "Speedup above 100 must reject."
                Expect.isError (TargetReplayPlan.tryCreate 50u 0UL [||] [| 1UL, 2UL |]) "A zero period must reject."
                Expect.isError (TargetReplayPlan.tryCreate 50u 1000000UL [||] [||]) "An empty eligible set must reject."
                Expect.isError
                    (TargetReplayPlan.tryCreate 50u 1000000UL [| 1UL, 10UL; 5UL, 20UL |] [| 1UL, 2UL |])
                    "Overlapping spans must reject."
                Expect.equal plan.Credit 50000UL "Selected credit must be the period times the speedup."
            }

            test "a valid journal reconciles exactly once" {
                let summary = replayed journal |> Result.defaultWith failwith
                Expect.equal summary.GlobalDelay 50700UL "Global delay must be the largest publish."
                Expect.equal summary.Locals [| 50000UL; 50700UL |] "Each worker local delay must match its events."
                Expect.equal (summary.SelectedSamples, summary.Credited) (1UL, 50000UL) "Credit must come from selected samples."
                Expect.equal (summary.Children, summary.MeasuredPayment) (2, 50700UL) "Payment must sum measured children."
                Expect.equal summary.MaxObservedDebt 50000UL "The largest observed debt must be reported."
                Expect.equal (TargetLocalReplay.effectiveDuration 1000000UL summary) (Ok 949300UL) "Effective duration subtracts global delay."
            }

            test "replay is independent of journal order" {
                Expect.equal (replayed (Array.rev journal)) (replayed journal) "The replay must order events by sequence."
            }

            test "sequence gaps and duplicates reject" {
                rejected (Array.append (Array.take 5 journal) [| { journal[5] with Sequence = 7UL } |]) "sequences" "A gap must reject."
                rejected (replace 5 (fun value -> { value with Sequence = 5UL })) "sequences" "A duplicate must reject."
                rejected (replace 0 (fun value -> { value with Worker = 2 })) "unknown worker" "An unknown worker must reject."
            }

            test "credit and local mismatches reject" {
                rejected (replace 0 (fun value -> { value with Amount = 40000UL })) "wrong credit" "Wrong selected credit must reject."
                rejected (replace 2 (fun value -> { value with Amount = 50000UL })) "wrong credit" "Credit for an unselected sample must reject."
                rejected (replace 3 (fun value -> { value with LocalBefore = 1UL })) "expects local delay" "A wrong prior local delay must reject."
                rejected (replace 1 (fun value -> { value with Amount = 40000UL })) "publish" "A publish of another value must reject."
            }

            test "payment rule violations reject" {
                rejected (replace 3 (fun value -> { value with Requested = 60000UL })) "payment rule" "A child above 50 us must reject."
                rejected (replace 3 (fun value -> { value with Ip = 0x9000UL })) "payment rule" "A child at an ineligible address must reject."
                rejected (replace 4 (fun value -> { value with Requested = 25000UL })) "payment rule" "A child above its observed debt must reject."
                rejected (replace 2 (fun value -> { value with ObservedGlobal = 123UL })) "no publish produced" "An unpublished observed global must reject."
            }

            test "a lost publication update rejects" {
                let lost = replace 5 (fun value -> { value with ObservedGlobal = 0UL })
                rejected lost "publish chain" "Two publishes from the same predecessor must reject."
            }

            test "the child limit is independent of observed debt" {
                let large = 1000000UL
                let events = [|
                    event 0 1UL TargetLedgerEventKind.Sample 0UL 0UL 50000UL 0UL selectedIp 1u
                    event 0 2UL TargetLedgerEventKind.Publish 0UL 50000UL 50000UL 0UL selectedIp 0u
                |]
                let wide =
                    TargetReplayPlan.tryCreate 50u (2UL * large) [| 0x1000UL, 0x2000UL |] [| 0x1000UL, 0x3000UL |]
                    |> Result.defaultWith failwith
                let journal = [|
                    event 0 1UL TargetLedgerEventKind.Sample 0UL 0UL large 0UL selectedIp 1u
                    event 0 2UL TargetLedgerEventKind.Publish 0UL large large 0UL selectedIp 0u
                    event 1 3UL TargetLedgerEventKind.Child large 0UL 50001UL 50001UL peerIp 0u
                |]
                Expect.isOk (replayed events) "A minimal valid journal must reconcile."
                match TargetLocalReplay.replay wide 2 journal with
                | Ok _ -> failtest "A child request of 50,001 ns must reject even with larger debt."
                | Error reason -> Expect.stringContains reason "payment rule" "The child limit must reject the request."
            }

            test "failures, failed waits, and zero requests reject" {
                let failure = Array.append journal [| event 1 7UL TargetLedgerEventKind.Failure 50700UL 50700UL 0UL 0UL peerIp 3u |]
                rejected failure "ledger failure" "A journaled ledger failure must reject."
                rejected (replace 3 (fun value -> { value with Detail = 1u })) "failed wait" "A failed child wait must reject."
                rejected (replace 3 (fun value -> { value with Requested = 0UL })) "payment rule" "A zero child request must reject."
                rejected (replace 0 (fun value -> { value with Detail = 0u })) "wrong credit" "A selected flag mismatch must reject."
                rejected (replace 0 (fun value -> { value with Kind = enum<TargetLedgerEventKind> 9 })) "unknown kind" "An unknown kind must reject."
            }

            test "deferred events reconcile and are counted" {
                let deferred = Array.append journal [| event 0 7UL TargetLedgerEventKind.Deferred 50700UL 50000UL 700UL 0UL 0x9000UL 0u |]
                let summary = replayed deferred |> Result.defaultWith failwith
                Expect.equal summary.Deferred 1 "A deferred event must be counted."
                let budget = Array.append journal [| event 0 7UL TargetLedgerEventKind.Deferred 50700UL 50000UL 700UL 0UL selectedIp 1u |]
                Expect.isOk (replayed budget) "A budget deferral at an eligible address must reconcile."
                let change (value: TargetLedgerEvent) = Array.append journal [| value |]
                let valid = deferred[6]
                rejected (change { valid with LocalBefore = 50700UL; ObservedGlobal = 50700UL; Amount = 0UL; Worker = 1 }) "Deferred event" "A zero-debt deferral must reject."
                rejected (change { valid with Amount = 600UL }) "Deferred event" "A deferral of the wrong debt must reject."
                rejected (change { valid with Ip = selectedIp }) "Deferred event" "An ineligible reason at an eligible address must reject."
                rejected (change { valid with Detail = 1u }) "Deferred event" "A budget reason at an ineligible address must reject."
            }

            test "a dependency wake skips to its post token" {
                let summary = barrier |> replayed |> Result.defaultWith failwith
                Expect.equal (summary.Blocks, summary.DependencyWakes, summary.SkippedDelay) (1, 1, 50000UL) "The wake must skip to the token."
                Expect.equal (summary.GlobalDelay, summary.Locals) (100000UL, [| 100000UL; 100000UL |]) "Credit after the post must be paid."
                Expect.equal summary.MaxObservedDebt 50000UL "Skipped delay must not count as debt, but credit after the post must."
            }

            test "wake protocol violations reject" {
                let barrierAt index change =
                    barrier |> Array.mapi (fun position value -> if position = index then change value else value)
                rejected (barrierAt 6 (fun value -> { value with Amount = 100000UL; Requested = 100000UL })) "does not match" "A skip to wake-time global must reject."
                rejected (barrierAt 6 (fun value -> { value with Amount = 40000UL; Requested = 40000UL })) "does not match" "An unposted token must reject."
                rejected (barrierAt 6 (fun value -> { value with Detail = 1u })) "does not match" "A timeout that skips delay must reject."
                rejected (barrierAt 0 (fun value -> { value with Kind = TargetLedgerEventKind.Sample; Ip = peerIp })) "has no block" "A wake without a block must reject."
                rejected (barrierAt 3 (fun value -> { value with Amount = 40000UL })) "does not post" "A post of another value must reject."
                rejected (Array.take 6 barrier) "still blocked" "A journal that ends blocked must reject."
                let doubled = [| barrier[0]; { barrier[0] with Sequence = 2UL } |]
                rejected doubled "wait protocol" "A second block must reject."
                let settles = [| barrier[0]; event 1 2UL TargetLedgerEventKind.Deferred 0UL 0UL 0UL 0UL 0x9000UL 0u |]
                rejected settles "settles a blocked worker" "Settlement while blocked must reject."
            }

            test "invalid replay inputs reject" {
                Expect.isError (TargetLocalReplay.replay plan 0 journal) "Zero workers must reject."
                let summary = replayed journal |> Result.defaultWith failwith
                Expect.isError (TargetLocalReplay.effectiveDuration 50700UL summary) "A non-positive effective duration must reject."
                Expect.isError
                    (TargetReplayPlan.tryCreate 50u 1000000UL (Array.init 17 (fun index -> uint64 (index * 2 + 1), uint64 (index * 2 + 2))) [| 1UL, 2UL |])
                    "More than 16 spans must reject."
            }
        ]
