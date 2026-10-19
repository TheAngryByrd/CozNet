# Target-local blocking queue

## Problem

Only `TargetBarrier` follows the Coz wake rule. A pipeline needs a bounded queue. Its consumer blocks when the queue is empty, and its producer blocks when the queue is full.

## Decision

`TargetQueue<'T>(capacity)` in `src/CozNet/TargetLocalProfiler.fs` is a bounded FIFO queue that uses the existing wait protocol. The rule and the bounds do not change.
- `Enqueue` takes a space, posts, and stores the token with the item. The post comes before the item is visible. In a thread without a profiler handle, it stores token 0 and makes no native call.
- `Dequeue` takes an item without a wait when one is present. Otherwise it calls `WaitBegin`, waits for an item, takes one, and calls `WaitEnd` with the token of that item.
  That item was posted before the dequeue, so its token is a posted value of global delay at or below the current global delay.
- After each dequeue, the consumer posts, stores the token in a release queue, and then releases one space.
- A lock holds the count of initial spaces and the release queue. A space acquisition with a wait takes a release token first. An acquisition without a wait takes an initial space first, and discards a release token when it takes one.
  A waiting producer that finds no release token ends its wait as spurious, so it skips no delay.
- `Enqueue` takes a space without a wait when one is free. Otherwise it calls `WaitBegin`, waits for a space, and calls `WaitEnd` with the token of its acquisition.
  That token comes from a release before the wake, so it is a posted value at or below the global delay.
  With one producer and one consumer, the token is that of the release that freed its space.
  With several producers or consumers, the token and the permit are not paired atomically. A producer can then take the token of a later or an earlier release.
  A later token can skip delay up to that release. An earlier token can skip too little, and a producer that finds no token skips nothing. Its debt then stays and can fail the debt bound.
  The qualification pipeline has one producer and one consumer.
- A cancelled token stops an enqueue or a dequeue before it takes a space or an item.
- A queue belongs to one run, and only registered workers of that run use it. Other use is not supported. It can fail the run, or skip delay by the wrong token.
- A timeout or a cancellation calls `WaitEnd` with reason `Timeout` or `Cancelled` and token 0, so no delay is skipped. The abort token of the run also cancels a wait.
- No native call happens while a thread holds a managed lock.

## Qualification

The workload `pipeline` runs a producer and a consumer for 120 ms in each experiment. The queue holds 4 items.
The producer repeats `Work.selected 40` and enqueues one item. When the counter stops, it enqueues a stop item.
The consumer dequeues, runs `Work.peer 20`, and visits the progress point, until it receives the stop item.
The producer is the bottleneck. So `Work.selected` gains 25%, 50%, and 50% at speedups 25, 50, and 75, because the consumer sets the rate at 50% and above. `Work.peer` gains 0%.
The consumer waits for most items, so most of its wakes are dependency wakes with a skip.
In a development run, the `Work.selected` experiment at 75% broke the 2 ms debt bound. The consumer is then the bottleneck and pays three quarters of the time of the producer. A qualification run can fail at that point, and the run then stays as evidence.

The sample modes are `pipeline-profile` and `plain-pipeline`. The runner `VerifyTargetLocalProfile.ps1 -Workload pipeline` and the build target `TargetLocalPipelineVerify` run the suite.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 45 and change none.

46. Each pipeline profile record holds the fixed design of criterion 38, with mode `pipeline-profile`, its seed, a 120 ms duration, and at least 100 visits in each passed experiment.
    Each passed experiment has `EffectiveNs` equal to `ElapsedNs` minus `GlobalNs`. Each prediction that the driver writes equals the prediction that the analyzer computes.
47. Each plain pipeline record has mode `plain-pipeline`, the rounds of its arm, a 120 ms duration, and at least 100 visits.
48. Each of the six points has 5 valid blocks. Its median absolute difference is at most 5 percentage points, and each block is within 10.
49. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local blocking](target-local-blocking.md), [target-local throughput](target-local-throughput.md), and [target-local status](target-local-status.md).
