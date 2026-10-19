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

    let private noGc = { Collections = 0; PauseNs = 0L }
    let private noLatency = struct (0L, 0UL, Array.empty<struct (int64 * int64)>)

    let private runResults run design = TargetCausalProfile.runWith (fun c s -> run c s, 1L, None, noGc, noLatency) design

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

            test "a latency duration outside 1 to 60000 ms is a parse error" {
                let parse duration = TargetCausalDesign.tryCreate binding 1000000UL symbols symbols [| 25u |] 1 0 0 (Latency duration)
                Expect.equal (parse 0, parse 60001) (Error [ InvalidDuration 0 ], Error [ InvalidDuration 60001 ]) "A duration outside 1 to 60000 ms must fail."
                Expect.isOk (parse 1) "1 ms must parse."
                Expect.isOk (parse 60000) "60000 ms must parse."
            }

            test "a safety stop fails its experiment and keeps the reason" {
                let design =
                    TargetCausalDesign.tryCreate binding 1000000UL symbols symbols [| 50u |] 1 0 0 (Throughput 100)
                    |> Result.defaultWith (fun errors -> failwith $"%A{errors}")
                let value = TargetCausalProfile.runWith (fun c s -> (result 1000UL true, 100L, (if c = "b" then Some "safety" else None), noGc, noLatency)) design
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
                let run c s = (result 1000UL true, (if s = 0u then 100L elif c = "a" then 200L else 100L), None, noGc, noLatency)
                let value = TargetCausalProfile.runWith run design
                let point c = value.Points |> Array.find (fun p -> p.Candidate = c)
                Expect.equal (point "a").Predictions [| Some 0.5 |] "200 visits in the same time must give a 50% shorter period."
                Expect.equal (point "b").Predictions [| Some 0.0 |] "The same visits must give no gain."
                Expect.equal (value.Experiments |> Array.map _.Visits |> Array.sort) [| 100L; 100L; 200L |] "Each experiment must keep its visits."
            }

            test "a latency prediction compares the virtual latency for each departure" {
                let design =
                    TargetCausalDesign.tryCreate binding 1000000UL symbols symbols [| 50u |] 1 0 0 (Latency 100)
                    |> Result.defaultWith (fun errors -> failwith $"%A{errors}")
                let publish time value = { Worker = 0; Sequence = time; Kind = TargetLedgerEventKind.Publish; ObservedGlobal = 0UL; LocalBefore = value; Amount = value
                                           Requested = 0UL; Ip = 0UL; Detail = 0u; Time = time }
                let withEvents events = { result 1000UL true with Events = events }
                let run c s =
                    if s = 0u then withEvents Array.empty, 2L, None, noGc, struct (2L, 0UL, [| struct (100L, 1100L); struct (200L, 1200L) |])
                    elif c = "a" then withEvents [| publish 500UL 300UL |], 4L, None, noGc, struct (4L, 0UL, [| struct (100L, 1100L); struct (2000L, 2900L); struct (3000L, 3100L); struct (3100L, 3200L) |])
                    else withEvents Array.empty, 2L, None, noGc, struct (2L, 0UL, Array.empty)
                let value = TargetCausalProfile.runWith run design
                let point c = value.Points |> Array.find (fun p -> p.Candidate = c)
                Expect.floatClose Accuracy.high (point "a").Predictions[0].Value (1.0 - (700.0 + 900.0 + 100.0 + 100.0) / 4.0 / 1000.0) "The prediction must compare the mean virtual latency, 1 - (VL / V) / (VL0 / V0)."
                Expect.equal (point "b").Predictions [| None |] "An experiment without transactions must give no prediction."
                Expect.equal (value.Experiments |> Array.map (fun e -> e.Speedup, e.VirtualLatencyNs) |> Array.sort) [| 0u, 2000UL; 50u, 0UL; 50u, 1800UL |] "Each experiment must keep its virtual latency."
                let failed = TargetCausalProfile.runWith (fun c s -> if c = "a" then { withEvents Array.empty with BindingError = Some "moved"; Passed = false }, 2L, None, noGc, struct (2L, 0UL, [| struct (0L, 10L) |]) else run c s) design
                Expect.equal (failed.Points |> Array.find (fun p -> p.Candidate = "a")).Predictions [| None |] "A failed experiment must give no prediction."
            }

            test "the virtual latency subtracts the global delay added during each transaction" {
                let publish sequence time value = { Worker = 1; Sequence = sequence; Kind = TargetLedgerEventKind.Publish; ObservedGlobal = 0UL; LocalBefore = value; Amount = value
                                                    Requested = 0UL; Ip = 0UL; Detail = 0u; Time = time }
                let sample = { publish 9UL 50UL 999UL with Kind = TargetLedgerEventKind.Sample }
                let events = [| publish 2UL 400UL 250UL; sample; publish 1UL 100UL 100UL; publish 3UL 0UL 5000UL |]
                let total, negative = TargetCausalProfile.virtualLatency events [| struct (50L, 150L); struct (150L, 450L); struct (100L, 400L); struct (300L, 390L); struct (390L, 401L) |]
                Expect.equal (total, negative) (0UL + 150UL + 150UL + 90UL, 1) "Each latency must subtract the global delay added from its begin to its end, a publish at a time counts from that time, and a negative value counts as 0."
                Expect.equal (TargetCausalProfile.virtualLatency Array.empty [| struct (10L, 30L) |]) (20UL, 0) "Without publishes, the virtual latency is the wall latency."
                let tied = [| publish 5UL 200UL 50UL; publish 4UL 200UL 80UL; publish 6UL 300UL 60UL |]
                Expect.equal (TargetCausalProfile.virtualLatency tied [| struct (100L, 250L); struct (250L, 350L) |]) (70UL + 100UL, 0)
                    "Equal times must count together, and a lower later value must not lower the global delay."
            }

            test "the hand-worked latency trace gives the virtual-time latency of each complete transaction" {
                let publish sequence time value = { Worker = 0; Sequence = sequence; Kind = TargetLedgerEventKind.Publish; ObservedGlobal = 0UL; LocalBefore = value; Amount = value
                                                    Requested = 0UL; Ip = 0UL; Detail = 0u; Time = time }
                let events = [| publish 1UL 25UL 10UL; publish 2UL 60UL 30UL |]
                let one (b: int64) (e: int64) = TargetCausalProfile.virtualLatency events [| struct (b, e) |]
                Expect.equal (one 5L 40L, one 20L 70L, one 60L 90L) ((25UL, 0), (20UL, 0), (30UL, 0))
                    "T1 must subtract the first step, T2 both steps, and T3, which begins at the second step, neither."
                Expect.equal (TargetCausalProfile.virtualLatency events [| struct (5L, 40L); struct (20L, 70L); struct (60L, 90L) |]) (75UL, 0)
                    "The three complete transactions must give the virtual-time integral of their occupancy, 115 - 40 ns."
            }

            test "a counter counts arrivals, departures, and latency" {
                let counter = TargetProgressCounter()
                let first = counter.Begin()
                let second = counter.Begin()
                Threading.Thread.Sleep 2
                counter.End first
                Expect.equal (counter.Arrivals, counter.Visits) (2L, 1L) "Each begin must count an arrival, and each end a departure."
                Expect.isGreaterThanOrEqual counter.LatencyNs 1_000_000UL "The latency must hold the time from the begin to the end."
                counter.End second
                Expect.equal counter.Visits 2L "Each end must count a visit."
                let ago (ms: int64) = Diagnostics.Stopwatch.GetTimestamp() - ms * Diagnostics.Stopwatch.Frequency / 1000L
                let summed = TargetProgressCounter()
                summed.End(ago 300L)
                summed.End(ago 500L)
                Expect.isGreaterThanOrEqual summed.LatencyNs 800_000_000UL "The latency must sum both transactions of 300 ms and 500 ms."
                Expect.isLessThan summed.LatencyNs 1_300_000_000UL "The latency must not count a transaction twice."
                let kept = summed.Transactions |> Array.map (fun struct (b, e) -> e - b) |> Array.sort
                Expect.equal kept.Length 2 "The counter must keep each transaction that ends."
                Expect.isTrue (kept[0] >= 300_000_000L && kept[1] >= 500_000_000L && kept[1] < 1_000_000_000L) "Each kept transaction must hold its begin and end in nanoseconds."
            }

            test "a counter settles the calling thread before it stamps a begin or an end" {
                let settled = Collections.Generic.List<int64>()
                let settle () =
                    let now = Diagnostics.Stopwatch.GetTimestamp()
                    settled.Add now
                    while Diagnostics.Stopwatch.GetTimestamp() = now do ()
                let counter = TargetProgressCounter settle
                let ticket = counter.Begin()
                Expect.equal settled.Count 1 "A begin must settle once."
                Expect.isLessThan settled[0] ticket "A begin must settle before it reads the clock."
                counter.End ticket
                Expect.equal settled.Count 2 "An end must settle once."
                let struct (_, finished) = counter.Transactions[0]
                let frequency = Diagnostics.Stopwatch.Frequency
                Expect.isLessThan (settled[1] / frequency * 1_000_000_000L + settled[1] % frequency * 1_000_000_000L / frequency) finished "An end must settle before it reads the clock."
                counter.Visit()
                Expect.equal settled.Count 2 "A visit must not settle."
            }

            test "an experiment or baseline with no visits gives no prediction and stays in the record" {
                let design =
                    TargetCausalDesign.tryCreate binding 1000000UL symbols symbols [| 50u |] 1 0 0 (Throughput 100)
                    |> Result.defaultWith (fun errors -> failwith $"%A{errors}")
                let value = TargetCausalProfile.runWith (fun c s -> (result 1000UL true, (if c = "a" && s = 50u then 0L else 100L), None, noGc, noLatency)) design
                Expect.equal ((value.Points |> Array.find (fun p -> p.Candidate = "a")).Predictions) [| None |] "Zero visits must give no prediction."
                Expect.equal (value.Experiments |> Array.filter (fun e -> e.Visits = 0L) |> Array.length) 1 "The experiment must stay."
                let baseline = TargetCausalProfile.runWith (fun _ s -> (result 1000UL true, (if s = 0u then 0L else 100L), None, noGc, noLatency)) design
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
                let rows = TargetCausalProfile.threads value Array.empty
                Expect.equal (rows |> Array.map (fun t -> t.CpuNs, t.HandlerNs, t.PaymentNs, t.SkipNs, t.Samples, t.Wakes, t.RunDelayNs, t.WallNs))
                    [| 10UL, 3UL, 11UL, 0UL, 2, 0, 7L, 0UL; 20UL, 3UL, 0UL, 4UL, 1, 1, 7L, 0UL |] "Each thread must keep its own totals."
            }

            test "a window payment counts the overlap of each child payment with each transaction" {
                let child worker time amount = { Worker = worker; Sequence = 0UL; Kind = TargetLedgerEventKind.Child; ObservedGlobal = 0UL; LocalBefore = 0UL
                                                 Amount = amount; Requested = 0UL; Ip = 0UL; Detail = 0u; Time = time }
                let windows = [| struct (100L, 200L); struct (150L, 300L) |]
                let paid events = TargetCausalProfile.windowPayments 2 events windows
                Expect.equal (paid [| child 1 130UL 20UL |]) [| 0UL; 20UL |] "A payment inside one window must count in full."
                Expect.equal (paid [| child 0 110UL 30UL |]) [| 10UL; 0UL |] "A payment across a window edge must count only its overlap."
                Expect.equal (paid [| child 0 450UL 50UL; child 1 90UL 40UL |]) [| 0UL; 0UL |] "A payment outside every window must count 0."
                Expect.equal (paid [| child 1 180UL 20UL |]) [| 0UL; 40UL |] "A payment in two windows must count in both."
                Expect.equal (paid [| { child 1 180UL 20UL with Kind = TargetLedgerEventKind.Wake } |]) [| 0UL; 0UL |] "A wake skip has no wall time and must count 0."
                let outcome = { RunDelayNs = 0L; StartRunDelayNs = 0L; StartTrace = Array.empty; WaitRunDelayNs = Array.empty; WaitRunDelaysDropped = 0
                                Result = TargetThreadResult(); FinishCode = 0; Error = ""; StartedTicks = 0L; FinishedTicks = 0L }
                let value = { result 100UL true with Threads = [| outcome; outcome |]; Events = [| child 1 130UL 20UL |] }
                Expect.equal (TargetCausalProfile.threads value windows |> Array.map _.WindowPaymentNs) [| 0UL; 20UL |] "Each thread record must hold its window payment."
            }

            test "an experiment keeps each ledger failure with its worker, debt, and times" {
                let event worker sequence kind time amount detail = { Worker = worker; Sequence = sequence; Kind = kind; ObservedGlobal = 0UL; LocalBefore = 0UL; Amount = amount
                                                                      Requested = 0UL; Ip = 0UL; Detail = detail; Time = time }
                let some (v: int64) = Nullable v
                let value =
                    { result 100UL false with
                        Events = [| event 1 3UL TargetLedgerEventKind.Wake 1700UL 0UL 0u; event 0 1UL TargetLedgerEventKind.Sample 1000UL 0UL 0u
                                    event 1 2UL TargetLedgerEventKind.Sample 1200UL 0UL 0u; event 1 4UL TargetLedgerEventKind.Failure 4200UL 2100000UL 1u
                                    event 0 5UL TargetLedgerEventKind.Failure 5000UL 2050000UL 1u |] }
                let rows (v: TargetProfileResult) = TargetCausalProfile.failures v |> Array.map (fun f -> f.Worker, f.Detail, f.DebtNs, f.TimeNs, f.GapNs, f.PreviousKind)
                Expect.equal (rows value) [| 1, 1u, 2100000UL, some 3200L, some 2500L, "Wake"; 0, 1u, 2050000UL, some 4000L, some 4000L, "Sample" |]
                    "Each failure must keep its worker, detail, debt, time, and gap."
                let untimed = { value with Events = Array.append value.Events [| event 1 6UL TargetLedgerEventKind.Failure 0UL 0UL 2u |] }
                Expect.equal (rows untimed |> Array.last) (1, 2u, 0UL, Nullable(), Nullable(), "") "A failure without a time must have unknown times."
                Expect.equal (rows untimed |> Array.take 2) (rows value) "A failure without a time must not move the origin."
                let buffered = { value with Events = Array.append value.Events [| event 0 7UL TargetLedgerEventKind.Child 6000UL 5UL 0u; event 0 8UL TargetLedgerEventKind.Sample 5800UL 0UL 0u
                                                                                  event 0 9UL TargetLedgerEventKind.Failure 6100UL 2010000UL 1u |] }
                Expect.equal (rows buffered |> Array.last) (0, 1u, 2010000UL, some 5100L, some 300L, "Sample") "The previous event must follow the journal sequence, not the time."
                Expect.isEmpty (TargetCausalProfile.failures { value with Events = value.Events |> Array.filter (fun e -> e.Kind <> TargetLedgerEventKind.Failure) })
                    "An experiment without a failure event must have no failure."
                Expect.isEmpty (TargetCausalProfile.failures { value with Events = Array.empty }) "An experiment without events must have no failure."
            }

            test "a failure keeps the latest events of all workers within 10 ms before it" {
                let event worker sequence kind time amount requested = { Worker = worker; Sequence = sequence; Kind = kind; ObservedGlobal = sequence * 10UL; LocalBefore = sequence * 5UL
                                                                         Amount = amount; Requested = requested; Ip = 0UL; Detail = 0u; Time = time }
                let early = event 0 1UL TargetLedgerEventKind.Sample 1_000_000UL 0UL 0UL
                let child = event 1 2UL TargetLedgerEventKind.Child 15_000_000UL 900_000UL 500_000UL
                let publish = event 1 3UL TargetLedgerEventKind.Publish 15_100_000UL 0UL 0UL
                let untimed = event 1 4UL TargetLedgerEventKind.Deferred 0UL 0UL 0UL
                let late = event 0 6UL TargetLedgerEventKind.Sample 16_000_000UL 0UL 0UL
                let own = event 0 4UL TargetLedgerEventKind.Sample 15_200_000UL 0UL 0UL
                let stamped = event 1 5UL TargetLedgerEventKind.Child 15_600_000UL 40_000UL 50_000UL
                let long = event 1 10UL TargetLedgerEventKind.Child 15_300_000UL 1_000_000UL 950_000UL
                let broken = { (event 1 11UL TargetLedgerEventKind.Child 15_350_000UL 3_000_000UL 50_000UL) with Detail = 1u }
                let untimed = { untimed with Sequence = 7UL }
                let failure = { (event 0 12UL TargetLedgerEventKind.Failure 15_500_000UL 2_100_000UL 0UL) with Detail = 1u }
                let late = { late with Sequence = 13UL }
                let value = { result 100UL false with Events = [| late; failure; broken; long; stamped; publish; untimed; own; early; child |] }
                let found = TargetCausalProfile.failures value |> Array.exactlyOne
                Expect.equal (found.Context |> Array.map (fun c -> c.Worker, c.Kind, c.ObservedGlobal, c.LocalBefore, c.Amount, c.Requested, c.Detail, c.OffsetNs))
                    [| 1, "Child", 20UL, 10UL, 900_000UL, 500_000UL, 0u, -500_000L; 1, "Publish", 30UL, 15UL, 0UL, 0UL, 0u, -400_000L
                       0, "Sample", 40UL, 20UL, 0UL, 0UL, 0u, -300_000L; 1, "Child", 50UL, 25UL, 40_000UL, 50_000UL, 0u, 100_000L
                       1, "Child", 100UL, 50UL, 1_000_000UL, 950_000UL, 0u, -200_000L; 1, "Child", 110UL, 55UL, 3_000_000UL, 50_000UL, 1u, -150_000L |]
                    "The context must keep the earlier timed events of all workers within 10 ms, in sequence order, with every field."
                Expect.equal found.ContextEvents 6 "The failure must count the events in its window."
                Expect.equal (found.Workers |> Array.map (fun w -> w.Worker, w.Events, w.Children, w.FailedChildren, w.MeasuredNs, w.RequestedNs, w.MaxOvershootNs, w.MaxOvershootOffsetNs, w.Publishes))
                    [| 0, 1, 0, 0, 0UL, 0UL, Nullable(), Nullable(), 0; 1, 5, 4, 1, 4_940_000UL, 1_550_000UL, Nullable 400_000L, Nullable -500_000L, 1 |]
                    "Each worker must summarize its window, with the completed child of the largest overshoot, not the longest or a failed child."
                Expect.equal (found.Workers[1].LastPublishLocal, found.Workers[1].LastPublishObserved, found.Workers[1].LastPublishOffsetNs) (Nullable 0UL, Nullable 30UL, Nullable -400_000L)
                    "Each worker must keep its last publish."
                let many = [| for i in 1..70 -> event (i % 2) (uint64 i) (if i <= 6 then TargetLedgerEventKind.Child else TargetLedgerEventKind.Sample) (14_000_000UL + uint64 i) (if i = 3 then 700UL else 0UL) 0UL |]
                let crowded = { result 100UL false with Events = Array.append many [| { failure with Sequence = 71UL } |] }
                let crowdedFailure = TargetCausalProfile.failures crowded |> Array.exactlyOne
                let kept = crowdedFailure.Context
                Expect.equal (kept.Length, kept[0].ObservedGlobal, (Array.last kept).ObservedGlobal) (64, 70UL, 700UL) "The context must keep only the 64 latest events."
                Expect.equal (crowdedFailure.ContextEvents, crowdedFailure.Workers |> Array.sumBy _.Events) (70, 70) "The count and the worker summaries must cover the whole window."
                Expect.equal (crowdedFailure.Workers |> Array.map (fun w -> w.Worker, w.Children, w.MeasuredNs, w.MaxOvershootNs)) [| 0, 3, 0UL, Nullable 0L; 1, 3, 700UL, Nullable 700L |]
                    "The worker summaries must include children outside the 64 kept events."
                let blind = { result 100UL false with Events = [| child; { failure with Time = 0UL } |] }
                Expect.isEmpty (TargetCausalProfile.failures blind |> Array.exactlyOne).Context "A failure without a time must have no context."
            }

            test "an experiment keeps its failures and GC values, and a thrown experiment marks them unknown" {
                let design =
                    TargetCausalDesign.tryCreate binding 1000000UL symbols symbols [| 50u |] 1 0 0 EndToEnd
                    |> Result.defaultWith (fun errors -> failwith $"%A{errors}")
                let failing =
                    { result 1000UL false with
                        Events = [| { Worker = 1; Sequence = 1UL; Kind = TargetLedgerEventKind.Failure; ObservedGlobal = 0UL; LocalBefore = 0UL; Amount = 2200000UL
                                      Requested = 0UL; Ip = 0UL; Detail = 1u; Time = 50UL } |] }
                let value = TargetCausalProfile.runWith (fun c s -> if c = "b" then failwith "boom" else ((if s = 50u then failing else result 1000UL true), 1L, None, { Collections = 3; PauseNs = 700L }, noLatency)) design
                let gc = value.Experiments |> Array.map (fun e -> e.Candidate, e.Speedup, e.Collections, e.GcPauseNs) |> Array.sort
                Expect.equal gc [| "a", 0u, 3, 700L; "a", 50u, 3, 700L; "b", 50u, -1, -1L |] "Each experiment must keep its GC values, and a thrown one must record -1."
                let failed = value.Experiments |> Array.find (fun e -> e.Candidate = "a" && e.Speedup = 50u)
                Expect.equal (failed.Failures |> Array.map (fun f -> f.Worker, f.DebtNs)) [| 1, 2200000UL |] "A failed experiment must keep its ledger failures."
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
