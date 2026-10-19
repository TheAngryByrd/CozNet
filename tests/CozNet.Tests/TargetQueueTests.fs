namespace CozNet.Tests

open System
open System.Threading
open CozNet
open Expecto

module TargetQueueTests =
    let private short = TimeSpan.FromMilliseconds 20.0

    [<Tests>]
    let tests =
        testList "target queue" [
            test "a queue gives items back in order" {
                let queue = TargetQueue<int> 4
                for value in 1..3 do queue.Enqueue value
                Expect.equal [ for _ in 1..3 -> queue.Dequeue() ] [ 1; 2; 3 ] "Items must leave in the order they entered."
                Expect.equal queue.Count 0 "The queue must be empty."
            }

            test "a full queue times out an enqueue and keeps its items" {
                let queue = TargetQueue<int> 2
                queue.Enqueue 1
                queue.Enqueue 2
                Expect.isFalse (queue.Enqueue(3, short, CancellationToken.None)) "A full queue must time out."
                Expect.equal (queue.Count, queue.Dequeue(), queue.Dequeue()) (2, 1, 2) "The timed-out item must not enter."
            }

            test "an empty queue times out a dequeue" {
                let queue = TargetQueue<int> 1
                Expect.equal (queue.Dequeue(short, CancellationToken.None)) ValueNone "An empty queue must time out."
            }

            test "a cancelled wait returns without an item" {
                let queue = TargetQueue<int> 1
                use source = new CancellationTokenSource()
                source.Cancel()
                Expect.equal (queue.Dequeue(Timeout.InfiniteTimeSpan, source.Token)) ValueNone "A cancelled dequeue must return no item."
                queue.Enqueue 1
                Expect.isFalse (queue.Enqueue(2, Timeout.InfiniteTimeSpan, source.Token)) "A cancelled enqueue must return false."
            }

            test "a cancelled token stops a wait even when an item or a space is free" {
                let queue = TargetQueue<int> 2
                queue.Enqueue 1
                use source = new CancellationTokenSource()
                source.Cancel()
                Expect.isFalse (queue.Enqueue(2, short, source.Token)) "A cancelled enqueue must not add an item."
                Expect.equal (queue.Dequeue(short, source.Token)) ValueNone "A cancelled dequeue must not take an item."
                Expect.equal (queue.Count, queue.Dequeue()) (1, 1) "The queue must keep its item."
            }

            test "a blocked producer and consumer move every item in order" {
                let queue = TargetQueue<int> 2
                use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 10.0)
                let received = Collections.Generic.List<int>()
                let consumer =
                    Thread((fun () ->
                        let mutable running = true
                        while running && received.Count < 1000 do
                            match queue.Dequeue(Timeout.InfiniteTimeSpan, deadline.Token) with
                            | ValueSome value -> received.Add value
                            | ValueNone -> running <- false), IsBackground = true)
                consumer.Start()
                let added = [ for value in 1..1000 -> queue.Enqueue(value, Timeout.InfiniteTimeSpan, deadline.Token) ]
                Expect.isTrue (consumer.Join(TimeSpan.FromSeconds 10.0)) "The consumer must finish."
                Expect.isTrue (List.forall id added) "Every enqueue must succeed before the deadline."
                Expect.equal (List.ofSeq received) [ 1..1000 ] "Every item must arrive once and in order."
                Expect.equal queue.Count 0 "The queue must be empty at the end."
            }

            test "two producers and one consumer move every item once" {
                let queue = TargetQueue<int> 2
                use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 10.0)
                let produce first =
                    Thread((fun () ->
                        for value in first .. 2 .. 1000 do
                            queue.Enqueue(value, Timeout.InfiniteTimeSpan, deadline.Token) |> ignore), IsBackground = true)
                let producers = [| produce 1; produce 2 |]
                producers |> Array.iter _.Start()
                let received = [ for _ in 1..1000 -> queue.Dequeue(Timeout.InfiniteTimeSpan, deadline.Token) ]
                Expect.isTrue (producers |> Array.forall (fun t -> t.Join(TimeSpan.FromSeconds 10.0))) "Both producers must finish."
                Expect.equal (received |> List.choose (function ValueSome v -> Some v | ValueNone -> None) |> List.sort) [ 1..1000 ] "Each item must arrive once."
            }

            test "a queue needs a capacity of at least one" {
                Expect.throwsT<ArgumentOutOfRangeException> (fun () -> TargetQueue<int> 0 |> ignore) "A zero capacity must fail."
            }
        ]
