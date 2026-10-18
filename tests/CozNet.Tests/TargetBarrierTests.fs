namespace CozNet.Tests

open System
open System.Threading
open CozNet
open Expecto

module TargetBarrierTests =
    [<Tests>]
    let tests =
        testList "target barrier" [
            test "workers pass phases together outside a profile" {
                let barrier = TargetBarrier 3
                let phases = 5
                let seen = Array.zeroCreate<int64> 3
                let order = Collections.Concurrent.ConcurrentQueue<int64 * int64>()
                let threads =
                    Array.init 3 (fun index ->
                        Thread(fun () ->
                            for _ in 1..phases do
                                let before = barrier.Phase
                                barrier.SignalAndWait()
                                order.Enqueue(before, barrier.Phase)
                                seen[index] <- seen[index] + 1L))
                threads |> Array.iter _.Start()
                threads |> Array.iter (fun thread -> Expect.isTrue (thread.Join(TimeSpan.FromSeconds 10.0)) "Each worker must finish.")
                Expect.equal seen [| 5L; 5L; 5L |] "Each worker must pass every phase."
                Expect.equal barrier.Phase 5L "The barrier must complete one phase per round."
                for before, after in order do
                    Expect.isGreaterThan after before "A worker must leave only after its phase completes."
            }

            test "a timeout returns false and still counts the arrival" {
                let barrier = TargetBarrier 2
                Expect.isFalse (barrier.SignalAndWait(TimeSpan.FromMilliseconds 20.0, CancellationToken.None)) "A lone worker must time out."
                Expect.isTrue (barrier.SignalAndWait(TimeSpan.FromSeconds 1.0, CancellationToken.None)) "The second arrival must complete the phase."
                Expect.equal barrier.Phase 1L "The timed-out arrival must count toward the phase."
            }

            test "cancellation returns false" {
                let barrier = TargetBarrier 2
                use source = new CancellationTokenSource()
                source.Cancel()
                Expect.isFalse (barrier.SignalAndWait(Timeout.InfiniteTimeSpan, source.Token)) "A cancelled wait must return false."
            }

            test "a run abort releases a waiting worker" {
                let barrier = TargetBarrier 2
                use abort = new CancellationTokenSource()
                abort.Cancel()
                TargetCurrent.Set(0n, 0UL, abort.Token)
                try
                    Expect.isFalse (barrier.SignalAndWait(TimeSpan.FromSeconds 10.0, CancellationToken.None)) "An aborted run must release the waiter."
                finally
                    TargetCurrent.Set(0n, 0UL, CancellationToken.None)
            }

            test "a recorded native error cancels the run" {
                use abort = new CancellationTokenSource()
                TargetCurrent.Set(0n, 0UL, abort)
                try
                    TargetCurrent.Record 5
                    Expect.isTrue abort.IsCancellationRequested "A native barrier error must cancel the run at once."
                    Expect.equal TargetCurrent.Error 5 "The first native error must be kept."
                finally
                    TargetCurrent.Set(0n, 0UL, CancellationToken.None)
            }

            test "bad sizes reject and one participant completes at once" {
                Expect.throwsT<ArgumentOutOfRangeException> (fun () -> TargetBarrier 0 |> ignore) "Zero participants must reject."
                let barrier = TargetBarrier 1
                Expect.isTrue (barrier.SignalAndWait(TimeSpan.Zero, CancellationToken.None)) "A single participant completes at once."
                Expect.equal barrier.Phase 1L "The phase must advance."
            }
        ]
