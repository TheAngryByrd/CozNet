namespace CozNet.Tests

open System
open System.Threading
open CozNet
open Expecto

module TargetSemaphoreTests =
    [<Tests>]
    let tests =
        testList "target semaphore" [
            test "a semaphore needs an initial count of at least 0" {
                Expect.throwsT<ArgumentOutOfRangeException> (fun () -> TargetSemaphore(-1) |> ignore) "A negative initial count must fail."
                Expect.equal (TargetSemaphore(0).CurrentCount, TargetSemaphore(2).CurrentCount) (0, 2) "The initial count must give the free permits."
            }

            test "a full semaphore raises on release and keeps its permits" {
                let semaphore = TargetSemaphore Int32.MaxValue
                Expect.throwsT<SemaphoreFullException> (fun () -> semaphore.Release()) "A release above Int32.MaxValue permits must fail."
                Expect.equal (semaphore.CurrentCount, semaphore.StoredTokens) (Int32.MaxValue, 0) "A failed release must not change the permits or store a token."
                Expect.isTrue (semaphore.Wait(TimeSpan.Zero, CancellationToken.None)) "A permit must be taken after the failed release."
                semaphore.Release()
                Expect.equal (semaphore.CurrentCount, semaphore.StoredTokens) (Int32.MaxValue, 1) "A release after a wait must store one token."
            }

            test "an empty semaphore times out a wait and takes no permit" {
                let semaphore = TargetSemaphore 0
                Expect.isFalse (semaphore.Wait(TimeSpan.FromMilliseconds 1.0, CancellationToken.None)) "An empty semaphore must time out."
                semaphore.Release()
                Expect.equal semaphore.CurrentCount 1 "A timed-out wait must not take a later permit."
                Expect.isTrue (semaphore.Wait(TimeSpan.Zero, CancellationToken.None)) "A free permit must be taken without a wait."
            }

            test "a cancelled wait returns false and takes no permit" {
                let semaphore = TargetSemaphore 1
                use source = new CancellationTokenSource()
                source.Cancel()
                Expect.isFalse (semaphore.Wait(Timeout.InfiniteTimeSpan, source.Token)) "A cancelled wait must return false."
                Expect.equal semaphore.CurrentCount 1 "A cancelled wait must not take the free permit."
                let empty = TargetSemaphore 0
                use later = new CancellationTokenSource(TimeSpan.FromMilliseconds 20.0)
                Expect.isFalse (empty.Wait(Timeout.InfiniteTimeSpan, later.Token)) "A wait cancelled while it blocks must return false."
                Expect.equal empty.CurrentCount 0 "A wait cancelled while it blocks must take no permit."
            }

            test "concurrent releases and waits lose and duplicate no permit" {
                let semaphore = TargetSemaphore 3
                use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 10.0)
                let mutable taken = 0
                let waiter () =
                    for _ in 1..2000 do
                        if semaphore.Wait(Timeout.InfiniteTimeSpan, deadline.Token) then
                            Interlocked.Increment(&taken) |> ignore
                let releaser () =
                    for _ in 1..2000 do
                        semaphore.Release()
                let threads = [| Thread(waiter, IsBackground = true); Thread(waiter, IsBackground = true); Thread(releaser, IsBackground = true); Thread(releaser, IsBackground = true) |]
                threads |> Array.iter _.Start()
                Expect.isTrue (threads |> Array.forall (fun t -> t.Join(TimeSpan.FromSeconds 10.0))) "All threads must finish."
                Expect.equal (taken, semaphore.CurrentCount) (4000, 3) "Each wait must take one permit, and the 3 initial permits must stay free."
            }

            test "two workers take turns through two semaphores" {
                let first = TargetSemaphore 0
                let second = TargetSemaphore 0
                let order = Collections.Concurrent.ConcurrentQueue<int>()
                let lead () =
                    for _ in 1..500 do
                        order.Enqueue 0
                        second.Release()
                        first.Wait()
                let follow () =
                    for _ in 1..500 do
                        second.Wait()
                        order.Enqueue 1
                        first.Release()
                let threads = [| Thread(lead, IsBackground = true); Thread(follow, IsBackground = true) |]
                threads |> Array.iter _.Start()
                Expect.isTrue (threads |> Array.forall (fun t -> t.Join(TimeSpan.FromSeconds 10.0))) "Both workers must finish."
                Expect.sequenceEqual (order.ToArray()) [| for _ in 1..500 do yield! [ 0; 1 ] |] "The workers must alternate."
            }
        ]
