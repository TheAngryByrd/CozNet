namespace CozNet.Tests

open System
open System.Threading
open CozNet
open Expecto

module TargetLockTests =
    [<Tests>]
    let tests =
        testList "target lock" [
            test "a lock admits one holder at a time" {
                let gate = TargetLock()
                use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 10.0)
                let mutable inside = 0
                let mutable overlap = false
                let mutable count = 0
                let worker () =
                    for _ in 1..2000 do
                        if gate.Enter(Timeout.InfiniteTimeSpan, deadline.Token) then
                            if Interlocked.Increment(&inside) > 1 then overlap <- true
                            count <- count + 1
                            Interlocked.Decrement(&inside) |> ignore
                            gate.Exit()
                let threads = [| for _ in 1..3 -> Thread(worker, IsBackground = true) |]
                threads |> Array.iter _.Start()
                Expect.isTrue (threads |> Array.forall (fun t -> t.Join(TimeSpan.FromSeconds 10.0))) "All workers must finish."
                Expect.equal (overlap, count) (false, 6000) "No two holders may overlap, and every entry must count."
            }

            test "a lock counts entries, waited entries, and handoff time" {
                let gate = TargetLock()
                gate.Enter()
                gate.Exit()
                gate.Enter()
                let waiter = Thread((fun () -> gate.Enter(); gate.Exit()), IsBackground = true)
                waiter.Start()
                let watch = Diagnostics.Stopwatch.StartNew()
                while not (waiter.ThreadState.HasFlag ThreadState.WaitSleepJoin) && watch.Elapsed < TimeSpan.FromSeconds 10.0 do
                    Thread.Sleep 1
                Expect.isTrue (waiter.ThreadState.HasFlag ThreadState.WaitSleepJoin) "The waiter must block on the lock."
                Thread.Sleep 30
                let released = Diagnostics.Stopwatch.StartNew()
                gate.Exit()
                Expect.isTrue (waiter.Join(TimeSpan.FromSeconds 10.0)) "The waiter must finish."
                let window = released.Elapsed.TotalNanoseconds
                Expect.equal (gate.Entries, gate.WaitedEntries) (3L, 1L) "Two free entries and one waited entry must count."
                Expect.isGreaterThan gate.HandoffNs 0L "A waited entry must add handoff time."
                Expect.isLessThanOrEqual (float gate.HandoffNs) window "Handoff time must start at the release, not at the wait."
            }

            test "a held lock times out another thread and keeps its holder" {
                let gate = TargetLock()
                gate.Enter()
                let mutable entered = true
                let other = Thread(fun () -> entered <- gate.Enter(TimeSpan.FromMilliseconds 20.0, CancellationToken.None))
                other.Start()
                Expect.isTrue (other.Join(TimeSpan.FromSeconds 10.0)) "The other thread must finish."
                Expect.isFalse entered "A held lock must time out the other thread."
                Expect.isTrue gate.IsHeld "The holder must keep the lock."
                gate.Exit()
                Expect.isFalse gate.IsHeld "Exit must release the lock."
            }

            test "a cancelled token stops an enter on a free lock" {
                let gate = TargetLock()
                use source = new CancellationTokenSource()
                source.Cancel()
                Expect.isFalse (gate.Enter(Timeout.InfiniteTimeSpan, source.Token)) "A cancelled enter must fail."
                Expect.isFalse gate.IsHeld "A cancelled enter must not take the lock."
            }

            test "a cancellation during a wait returns without the lock and keeps the holder" {
                let gate = TargetLock()
                gate.Enter()
                use source = new CancellationTokenSource()
                let mutable entered = true
                let other = Thread((fun () -> entered <- gate.Enter(Timeout.InfiniteTimeSpan, source.Token)), IsBackground = true)
                other.Start()
                Thread.Sleep 20
                source.Cancel()
                Expect.isTrue (other.Join(TimeSpan.FromSeconds 10.0)) "The waiter must return after the cancellation."
                Expect.isFalse entered "A cancelled waiter must not take the lock."
                gate.Exit()
                Expect.isTrue (gate.Enter(TimeSpan.FromSeconds 1.0, CancellationToken.None)) "The lock must stay usable."
                gate.Exit()
            }

            test "only the holder can exit and the lock does not recurse" {
                let gate = TargetLock()
                Expect.throwsT<SynchronizationLockException> (fun () -> gate.Exit()) "Exit without a holder must fail."
                gate.Enter()
                Expect.throwsT<LockRecursionException> (fun () -> gate.Enter()) "A second enter by the holder must fail."
                let mutable failed = false
                let other = Thread(fun () -> try gate.Exit() with :? SynchronizationLockException -> failed <- true)
                other.Start()
                Expect.isTrue (other.Join(TimeSpan.FromSeconds 10.0)) "The other thread must finish."
                Expect.isTrue failed "Another thread must not release the lock."
                gate.Exit()
            }
        ]
