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

            test "a spin lock admits one holder and rejects a spin outside 0 to 1 ms" {
                Expect.throwsT<ArgumentOutOfRangeException> (fun () -> TargetLock(TimeSpan.FromMilliseconds 2.0) |> ignore) "A spin above 1 ms must fail."
                Expect.throwsT<ArgumentOutOfRangeException> (fun () -> TargetLock(TimeSpan.FromTicks -1L) |> ignore) "A negative spin must fail."
                let gate = TargetLock(TimeSpan.FromMilliseconds 1.0)
                use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 10.0)
                let mutable inside = 0
                let mutable overlap = false
                let worker () =
                    for _ in 1..1000 do
                        if gate.Enter(Timeout.InfiniteTimeSpan, deadline.Token) then
                            if Interlocked.Increment(&inside) > 1 then overlap <- true
                            Interlocked.Decrement(&inside) |> ignore
                            gate.Exit()
                let threads = [| for _ in 1..3 -> Thread(worker, IsBackground = true) |]
                threads |> Array.iter _.Start()
                Expect.isTrue (threads |> Array.forall (fun t -> t.Join(TimeSpan.FromSeconds 10.0))) "All workers must finish."
                Expect.equal (overlap, gate.Entries) (false, 3000L) "No two holders may overlap, and every entry must count."
            }

            test "a spin keeps the timeout budget of a held lock" {
                let gate = TargetLock(TimeSpan.FromMilliseconds 1.0)
                gate.Enter()
                let mutable entered = false
                let mutable fastest = TimeSpan.MaxValue
                let other =
                    Thread((fun () ->
                        for _ in 1..20 do
                            let watch = Diagnostics.Stopwatch.StartNew()
                            entered <- entered || gate.Enter(TimeSpan.FromMilliseconds 0.2, CancellationToken.None)
                            fastest <- min fastest watch.Elapsed), IsBackground = true)
                other.Start()
                Expect.isTrue (other.Join(TimeSpan.FromSeconds 10.0)) "The other thread must finish."
                Expect.isFalse entered "A held lock must time out."
                Expect.isLessThan fastest (TimeSpan.FromMilliseconds 0.9) "A 0.2 ms timeout must stop the 1 ms spin in at least one of 20 attempts."
                gate.Exit()
            }

            test "a spin lock waiter takes the lock as a waited entry when the holder releases" {
                let gate = TargetLock(TimeSpan.FromMilliseconds 1.0)
                gate.Enter()
                let mutable entered = false
                let other = Thread((fun () -> entered <- gate.Enter(TimeSpan.FromSeconds 5.0, CancellationToken.None)), IsBackground = true)
                other.Start()
                let watch = Diagnostics.Stopwatch.StartNew()
                let mutable waiting = false
                while not waiting && watch.Elapsed < TimeSpan.FromSeconds 5.0 do
                    waiting <- other.ThreadState.HasFlag ThreadState.WaitSleepJoin
                    if not waiting then Thread.Sleep 1
                Expect.isTrue waiting "The waiter must reach its wait before the holder exits."
                gate.Exit()
                Expect.isTrue (other.Join(TimeSpan.FromSeconds 10.0)) "The waiter must finish."
                Expect.isTrue entered "The waiter must take the lock."
                Expect.isTrue gate.IsHeld "The waiter must hold the lock."
                Expect.equal gate.WaitedEntries 1L "The waiter must take the lock as a waited entry."
            }

            test "a cancelled spin lock waiter returns without the lock" {
                let gate = TargetLock(TimeSpan.FromMilliseconds 1.0)
                gate.Enter()
                use source = new CancellationTokenSource(TimeSpan.FromMilliseconds 0.3)
                let mutable entered = true
                let other = Thread((fun () -> entered <- gate.Enter(Timeout.InfiniteTimeSpan, source.Token)), IsBackground = true)
                other.Start()
                Expect.isTrue (other.Join(TimeSpan.FromSeconds 10.0)) "The waiter must return after the cancellation."
                Expect.isFalse entered "A cancelled waiter must not take the lock."
                gate.Exit()
            }

            test "a held spin lock still rejects a negative timeout other than infinite" {
                let gate = TargetLock(TimeSpan.FromMilliseconds 1.0)
                gate.Enter()
                let mutable raised = false
                let other = Thread((fun () -> try gate.Enter(TimeSpan.FromMilliseconds -2.0, CancellationToken.None) |> ignore with :? ArgumentOutOfRangeException -> raised <- true), IsBackground = true)
                other.Start()
                Expect.isTrue (other.Join(TimeSpan.FromSeconds 10.0)) "The other thread must finish."
                Expect.isTrue raised "A -2 ms timeout must raise ArgumentOutOfRangeException."
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
