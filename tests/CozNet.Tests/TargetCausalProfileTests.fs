namespace CozNet.Tests

open System
open CozNet
open Expecto

module TargetCausalProfileTests =
    let private symbols = [| "a"; "b" |]

    let private binding =
        CodeBinding.tryCapture [ "0x1000 10 a[Optimized]"; "0x2000 10 b[Optimized]" ] symbols |> Result.defaultWith failwith

    let private designOf eligible candidates speedups blocks seed =
        TargetCausalDesign.tryCreate binding 1000000UL eligible candidates speedups blocks seed 0 EndToEnd
        |> Result.defaultWith (fun errors -> failwith $"%A{errors}")

    let private result effective passed : TargetProfileResult =
        { ElapsedNs = effective + 10UL; GlobalNs = 10UL; EffectiveNs = effective; Experiment = TargetExperimentResult(); CloseCode = 0
          Threads = Array.empty; Events = Array.empty; Replay = Error "unused"; BindingError = (if passed then None else Some "moved"); Passed = passed }

    let private summary : TargetReplaySummary =
        { GlobalDelay = 10UL; Locals = Array.empty; SelectedSamples = 0UL; Credited = 0UL; Publishes = 0; Children = 0; MeasuredPayment = 0UL
          Deferred = 0; Failures = 0; MaxObservedDebt = 0UL; Blocks = 0; DependencyWakes = 0; SkippedDelay = 0UL }

    let private runResults run design = TargetCausalProfile.runWith (fun c s -> run c s, 1L, None) design

    let private profile run blocks =
        runResults run (designOf symbols symbols [| 25u; 50u |] blocks 7)

    [<Tests>]
    let tests =
        testList "target causal profile" [
            test "each block runs one baseline and each candidate at each speedup once" {
                let calls = Collections.Generic.List<string * uint32>()
                let value = profile (fun c s -> calls.Add((c, s)); result 100UL true) 3
                for block in 1..3 do
                    let arms = value.Experiments |> Array.filter (fun e -> e.Block = block)
                    Expect.equal (arms |> Array.map _.Order) [| 0..4 |] $"Block {block} must record its run order."
                    Expect.equal (arms |> Array.filter (fun e -> e.Speedup = 0u) |> Array.length) 1 $"Block {block} must hold one baseline."
                    Expect.equal (arms |> Array.filter (fun e -> e.Speedup > 0u) |> Array.map (fun e -> e.Candidate, e.Speedup) |> Array.sort)
                        [| "a", 25u; "a", 50u; "b", 25u; "b", 50u |] $"Block {block} must hold each point once."
                Expect.equal calls.Count 16 "The warm-up and each experiment must run once."
                Expect.equal calls[0] ("a", 0u) "The warm-up must run first, at speedup 0, with the first candidate."
            }

            test "the warm-up stays in the result and no prediction uses it" {
                let mutable first = true
                let run _ s =
                    let effective = if first then 1000UL elif s = 0u then 200UL else 150UL
                    first <- false
                    result effective true
                let value = profile run 1
                Expect.equal (value.Warmup.Block, value.Warmup.Order, value.Warmup.Speedup, value.Warmup.EffectiveNs) (0, -1, 0u, 1000UL) "The warm-up must be kept."
                Expect.isTrue (value.Points |> Array.forall (fun p -> p.Predictions = [| Some 0.25 |])) "Predictions must use the block baseline, not the warm-up."
                Expect.equal value.Experiments.Length 5 "The warm-up must not count as a design experiment."
            }

            test "the same seed gives the same order and another seed can differ" {
                let order seed =
                    (runResults (fun _ _ -> result 100UL true) (designOf symbols symbols [| 25u; 50u; 75u |] 4 seed)).Experiments
                    |> Array.map (fun e -> e.Candidate, e.Speedup)
                Expect.equal (order 7) (order 7) "A seed must fix the order."
                Expect.notEqual (order 7) (order 8) "These two seeds must give different orders."
            }

            test "a prediction is one minus the effective duration over the block baseline" {
                let value = profile (fun c s -> result (if s = 0u then 200UL elif c = "a" then 150UL else 200UL) true) 2
                let point c s = value.Points |> Array.find (fun p -> p.Candidate = c && p.Speedup = s)
                Expect.equal (point "a" 25u).Predictions [| Some 0.25; Some 0.25 |] "Candidate a must gain 25%."
                Expect.equal (point "b" 50u).MedianPrediction (Some 0.0) "Candidate b must gain nothing."
                Expect.equal (point "a" 50u).ValidBlocks 2 "Both blocks must be valid."
            }

            test "each block uses its own baseline and the median of an even count is the mean of the middle pair" {
                let calls = Collections.Generic.Dictionary<int, int>()
                let mutable block = -1
                let run c s =
                    if s = 0u then block <- block + 1
                    result (if s = 0u then [| 300UL; 200UL; 400UL; 100UL; 1000UL |][block] else 100UL) true
                let value = runResults run (designOf symbols [| "a" |] [| 25u |] 4 7)
                let point = value.Points |> Array.exactlyOne
                Expect.equal point.Predictions [| Some 0.5; Some 0.75; Some 0.0; Some 0.9 |] "Each block must divide by its own baseline."
                Expect.equal point.MedianPrediction (Some 0.625) "The median of four values must be the mean of the middle two."
            }

            test "a failed run keeps its binding, replay, ledger, close, and thread errors" {
                let failing =
                    { result 100UL false with
                        Replay = Error "gap"; CloseCode = 5
                        Experiment = TargetExperimentResult(Failure = 1u)
                        Threads = [| { RunDelayNs = 0L; StartRunDelayNs = 0L; StartTrace = Array.empty; WaitRunDelayNs = Array.empty; WaitRunDelaysDropped = 0
                                       Result = TargetThreadResult(Complete = 1, Quiescent = 1); FinishCode = 0; Error = "work boom"; StartedTicks = 0L; FinishedTicks = 0L } |] }
                Expect.equal (TargetCausalProfile.failure failing) "moved; replay: gap; ledger failure 1; close 5; thread 0: work boom" "Every cause must stay in the error."
                let thread = TargetThreadResult(Complete = 1, Quiescent = 1, Lost = 2UL, MaxHandlerNs = 6000000UL)
                let native =
                    { result 100UL true with
                        Passed = false; Replay = Error "gap"; Experiment = TargetExperimentResult(ForeignSignals = 3UL, SigioFallbacks = 4UL)
                        Threads = [| { failing.Threads[0] with Error = ""; FinishCode = 9; Result = thread } |] }
                Expect.equal (TargetCausalProfile.failure native)
                    "replay: gap; foreign signals 3; SIGIO fallbacks 4; thread 0: finish 9; thread 0: lost 2; thread 0: handler interval 6000000 ns"
                    "Each native cause must stay in the error."
                let mismatch =
                    { result 0UL true with
                        Passed = false; GlobalNs = 11UL; Replay = Ok summary; Experiment = TargetExperimentResult(Sequences = 3UL)
                        Threads = [| { failing.Threads[0] with Error = ""; Result = TargetThreadResult() } |] }
                Expect.equal (TargetCausalProfile.failure mismatch)
                    "replay global delay 10 differs from native 11; native sequences 3 differ from 0 events; effective duration 0; thread 0: incomplete epoch; thread 0: not quiescent"
                    "Each replay and epoch cause must stay in the error."
                Expect.equal (TargetCausalProfile.failure { result 100UL true with Passed = false; Replay = Ok summary; GlobalNs = 10UL }) "The experiment did not pass." "A run with no named cause must say so."
            }

            test "a failed experiment invalidates only its own point and block and keeps its error" {
                let mutable count = 0
                let run c s =
                    count <- count + 1
                    if c = "b" && s = 50u && count <= 6 then result 100UL false else result 100UL true
                let value = profile run 2
                let failed = value.Experiments |> Array.filter (fun e -> not e.Passed)
                Expect.equal (failed |> Array.map (fun e -> e.Block, e.Candidate, e.Speedup, e.Error)) [| 1, "b", 50u, "moved; replay: unused" |] "The failure must stay in the record."
                let point = value.Points |> Array.find (fun p -> p.Candidate = "b" && p.Speedup = 50u)
                Expect.equal (point.Predictions, point.ValidBlocks) ([| None; Some 0.0 |], 1) "Only block 1 of that point must be invalid."
                Expect.isTrue (value.Points |> Array.filter (fun p -> p <> point) |> Array.forall (fun p -> p.ValidBlocks = 2)) "Other points must stay valid."
            }

            test "a failed baseline invalidates every point of its block" {
                let value = profile (fun _ s -> if s = 0u then result 100UL false else result 100UL true) 1
                Expect.isTrue (value.Points |> Array.forall (fun p -> p.ValidBlocks = 0 && p.MedianPrediction = None)) "No point may use a failed baseline."
            }

            test "an exception in an experiment is recorded as a failed experiment" {
                let value = profile (fun c s -> if c = "a" && s = 25u then raise (InvalidOperationException "boom") else result 100UL true) 1
                let failed = value.Experiments |> Array.filter (fun e -> not e.Passed)
                Expect.equal (failed |> Array.map (fun e -> e.Candidate, e.Speedup, e.Error)) [| "a", 25u, "InvalidOperationException: boom" |]
                    "The exception type and message must stay in the record."
            }

            test "an invalid design returns each parse error and no design" {
                let errors eligible candidates speedups blocks =
                    match TargetCausalDesign.tryCreate binding 1000000UL eligible candidates speedups blocks 0 0 EndToEnd with
                    | Ok _ -> []
                    | Error values -> values
                Expect.equal (errors symbols [| "a"; "a" |] [| 25u |] 1) [ DuplicateCandidate "a" ] "A repeated candidate must fail."
                Expect.equal (errors symbols [| "c" |] [| 25u |] 1) [ UnboundCandidate "c"; IneligibleCandidate "c" ] "An unbound candidate must fail."
                Expect.equal (errors [| "b" |] [| "a" |] [| 25u |] 1) [ IneligibleCandidate "a" ] "An ineligible candidate must fail."
                Expect.equal (errors symbols symbols [| 0u; 101u |] 1) [ SpeedupOutOfRange 0u; SpeedupOutOfRange 101u ] "Each speedup must be from 1 to 100."
                Expect.equal (errors symbols symbols [| 25u; 25u |] 1) [ DuplicateSpeedup 25u ] "A repeated speedup must fail."
                Expect.equal (errors symbols symbols [| 25u |] 0) [ NoBlocks 0 ] "Zero blocks must fail."
                Expect.equal (errors symbols [||] [||] 1) [ NoCandidates; NoSpeedups ] "Empty candidates and speedups must fail."
                Expect.equal (errors [| "a"; "z" |] [| "a" |] [| 25u |] 1) [ UnboundEligible "z" ] "An unbound eligible symbol must fail."
                Expect.isTrue
                    (match TargetCausalDesign.tryCreate binding 0UL symbols symbols [| 25u |] 1 0 0 EndToEnd with Error [ PlanRejected _ ] -> true | _ -> false)
                    "A period that the replay plan rejects must fail."
            }

            test "a throughput duration outside 1 to 60000 ms is a parse error" {
                let parse duration = TargetCausalDesign.tryCreate binding 1000000UL symbols symbols [| 25u |] 1 0 0 (Throughput duration)
                Expect.equal (parse 0) (Error [ InvalidDuration 0 ]) "A zero duration must fail."
                Expect.equal (parse 60001) (Error [ InvalidDuration 60001 ]) "A duration above 60000 ms must fail."
                Expect.equal (parse Int32.MaxValue) (Error [ InvalidDuration Int32.MaxValue ]) "The largest duration must fail."
                Expect.isOk (parse 1) "1 ms must parse."
                Expect.isOk (parse 60000) "60000 ms must parse."
            }

            test "a safety stop fails its experiment and keeps the reason" {
                let design =
                    TargetCausalDesign.tryCreate binding 1000000UL symbols symbols [| 50u |] 1 0 0 (Throughput 100)
                    |> Result.defaultWith (fun errors -> failwith $"%A{errors}")
                let value = TargetCausalProfile.runWith (fun c s -> (result 1000UL true, 100L, (if c = "b" then Some "safety" else None))) design
                let stopped = value.Experiments |> Array.filter (fun e -> not e.Passed)
                Expect.equal (stopped |> Array.map (fun e -> e.Candidate, e.Error)) [| "b", "safety" |] "Only the stopped experiment must fail, with its reason."
            }

            test "the guard stops the counter and reports when the action outlasts the delay" {
                let counter = TargetProgressCounter()
                let value, fired = TargetCausalProfile.guard (TimeSpan.FromMilliseconds 20.0) counter (fun () -> (while not counter.Stopped do Threading.Thread.Sleep 1); 7)
                Expect.equal (value, fired, counter.Stopped) (7, true, true) "A late action must be stopped and reported."
                let quick = TargetProgressCounter()
                let value, fired = TargetCausalProfile.guard (TimeSpan.FromSeconds 30.0) quick (fun () -> 8)
                Expect.equal (value, fired, quick.Stopped) (8, false, false) "A quick action must not be stopped."
            }

            test "a throughput prediction compares the effective time for each visit" {
                let design =
                    TargetCausalDesign.tryCreate binding 1000000UL symbols symbols [| 50u |] 1 0 0 (Throughput 100)
                    |> Result.defaultWith (fun errors -> failwith $"%A{errors}")
                let run c s = (result 1000UL true, (if s = 0u then 100L elif c = "a" then 200L else 100L), None)
                let value = TargetCausalProfile.runWith run design
                let point c = value.Points |> Array.find (fun p -> p.Candidate = c)
                Expect.equal (point "a").Predictions [| Some 0.5 |] "200 visits in the same time must give a 50% shorter period."
                Expect.equal (point "b").Predictions [| Some 0.0 |] "The same visits must give no gain."
                Expect.equal (value.Experiments |> Array.map _.Visits |> Array.sort) [| 100L; 100L; 200L |] "Each experiment must keep its visits."
            }

            test "an experiment or baseline with no visits gives no prediction and stays in the record" {
                let design =
                    TargetCausalDesign.tryCreate binding 1000000UL symbols symbols [| 50u |] 1 0 0 (Throughput 100)
                    |> Result.defaultWith (fun errors -> failwith $"%A{errors}")
                let value = TargetCausalProfile.runWith (fun c s -> (result 1000UL true, (if c = "a" && s = 50u then 0L else 100L), None)) design
                Expect.equal ((value.Points |> Array.find (fun p -> p.Candidate = "a")).Predictions) [| None |] "Zero visits must give no prediction."
                Expect.equal (value.Experiments |> Array.filter (fun e -> e.Visits = 0L) |> Array.length) 1 "The experiment must stay."
                let baseline = TargetCausalProfile.runWith (fun _ s -> (result 1000UL true, (if s = 0u then 0L else 100L), None)) design
                Expect.isTrue (baseline.Points |> Array.forall (fun p -> p.ValidBlocks = 0)) "A baseline with no visits must give no prediction."
            }

            test "the throughput monitor stops the counter after the duration once a worker works" {
                let counter = TargetProgressCounter()
                let watch = Diagnostics.Stopwatch.StartNew()
                let mutable polls = 0
                TargetCausalProfile.stopAfter 20 counter (fun () -> polls <- polls + 1; polls > 3) (fun () -> true)
                Expect.isTrue counter.Stopped "The counter must stop."
                Expect.isGreaterThanOrEqual watch.ElapsedMilliseconds 19L "The monitor must wait for the duration."
                let idle = TargetProgressCounter()
                TargetCausalProfile.stopAfter 10000 idle (fun () -> false) (fun () -> false)
                Expect.isTrue idle.Stopped "A run that ends before any work must still stop the counter."
            }

            test "the throughput monitor stops the counter when it fails" {
                let counter = TargetProgressCounter()
                Expect.throwsT<InvalidOperationException> (fun () ->
                    TargetCausalProfile.stopAfter 10 counter (fun () -> raise (InvalidOperationException "interrupted")) (fun () -> true)) "The failure must reach the caller."
                Expect.isTrue counter.Stopped "A failed monitor must still stop the workers."
            }

            test "a counter counts each visit and starts unstopped" {
                let counter = TargetProgressCounter()
                Expect.isFalse counter.Stopped "A new counter must not be stopped."
                for _ in 1..3 do counter.Visit()
                Expect.equal counter.Visits 3L "Each visit must count."
            }

            test "the safety stop comes long after the duration" {
                Expect.equal (TargetCausalProfile.safetyDelay 120) (TimeSpan.FromMilliseconds 2200.0) "The safety stop must wait 1 s plus ten times the duration."
            }

            test "an experiment keeps the payment, skip, samples, and wakes of each thread" {
                let event worker kind amount = { Worker = worker; Sequence = 0UL; Kind = kind; ObservedGlobal = 0UL; LocalBefore = 0UL; Amount = amount
                                                 Requested = 0UL; Ip = 0UL; Detail = 0u; Time = 0UL }
                let outcome cpu = { RunDelayNs = 7L; StartRunDelayNs = 0L; StartTrace = Array.empty; WaitRunDelayNs = Array.empty; WaitRunDelaysDropped = 0
                                    Result = TargetThreadResult(Value = cpu, TotalHandlerNs = 3UL); FinishCode = 0; Error = ""; StartedTicks = 0L; FinishedTicks = 0L }
                let value =
                    { result 100UL true with
                        Threads = [| outcome 10UL; outcome 20UL |]
                        Events = [| event 0 TargetLedgerEventKind.Child 5UL; event 0 TargetLedgerEventKind.Child 6UL; event 1 TargetLedgerEventKind.Wake 4UL
                                    event 1 TargetLedgerEventKind.Sample 0UL; event 0 TargetLedgerEventKind.Sample 0UL; event 0 TargetLedgerEventKind.Sample 0UL |] }
                let rows = TargetCausalProfile.threads value
                Expect.equal (rows |> Array.map (fun t -> t.CpuNs, t.HandlerNs, t.PaymentNs, t.SkipNs, t.Samples, t.Wakes, t.RunDelayNs, t.WallNs))
                    [| 10UL, 3UL, 11UL, 0UL, 2, 0, 7L, 0UL; 20UL, 3UL, 0UL, 4UL, 1, 1, 7L, 0UL |] "Each thread must keep its own totals."
            }

            test "a rotation shifts the order of each block and keeps every experiment" {
                let order rotation =
                    let design =
                        TargetCausalDesign.tryCreate binding 1000000UL symbols symbols [| 25u; 50u; 75u |] 3 7 rotation EndToEnd
                        |> Result.defaultWith (fun errors -> failwith $"%A{errors}")
                    (runResults (fun _ _ -> result 100UL true) design).Experiments
                    |> Array.map (fun e -> e.Block, e.Candidate, e.Speedup, e.Order)
                let blocks rotation b = order rotation |> Array.filter (fun (block, _, _, _) -> block = b) |> Array.sortBy (fun (_, _, _, o) -> o)
                                      |> Array.map (fun (_, c, s, _) -> c, s)
                let first = blocks 0 1
                let rotated = Array.append first[1..] first[..0]
                Expect.equal (blocks 0 2) rotated "Block 2 must rotate the base order by one place."
                Expect.equal (blocks 1 1) rotated "Rotation 1 must rotate block 1 by one place."
                Expect.equal (Array.sort (blocks 1 3)) (Array.sort first) "A rotation must keep every experiment."
            }

            test "a rotation outside the experiment count is a parse error" {
                let parse rotation = TargetCausalDesign.tryCreate binding 1000000UL symbols symbols [| 25u |] 1 0 rotation EndToEnd
                Expect.equal (parse -1) (Error [ InvalidRotation -1 ]) "A negative rotation must fail."
                Expect.equal (parse 3) (Error [ InvalidRotation 3 ]) "A rotation equal to the experiment count must fail."
                Expect.isOk (parse 2) "The last valid rotation must parse."
            }

            test "a design keeps copies of its inputs" {
                let candidates = [| "a"; "b" |]
                let design = designOf symbols candidates [| 25u |] 1 0
                candidates[0] <- "b"
                Expect.equal design.Candidates [| "a"; "b" |] "A later change to the input array must not change the design."
            }
        ]
