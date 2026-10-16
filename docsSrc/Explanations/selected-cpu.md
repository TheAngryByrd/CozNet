# CPU time and the prediction gap

Nominal-event credit matches the inspected native Coz event rule, but the first four-arm comparison still fails the accuracy target.
Correct accounting does not prove that the profiler preserves application performance.

## What elapsed time cannot identify

The nominal treatment with phase timing records about 12.3 ms in selected pauses and 38.9 ms in peer service calls.
Those intervals can overlap. They include scheduling delays and observer cost.
They do not identify the cause of a gap in a separate comparison.

A selected-thread CPU interval helps distinguish CPU consumption from elapsed time spent off CPU.
The same observation in both physical and sampled arms also shows how their CPU costs differ for the declared work.
It does not assign CPU cost to original arithmetic, injected instructions, or individual profiler helpers.
Such attribution needs a separate controlled experiment.

## First CPU capture

All four arms complete with valid CPU evidence and unchanged inputs.
The sampled zero arm consumes 221.9 ms of selected-thread CPU. The sampled half arm consumes 277.0 ms.
Elapsed time between the CPU readings exceeds CPU advance by less than 0.025 ms in each sampled arm.
This capture therefore points to additional CPU consumption rather than substantial descheduling within those brackets.

The CPU difference does not isolate profiler overhead or its cause.
The sampled effective elapsed reduction remains about 21.5%, against about 51.5% physical elapsed reduction.
Recurring probe, drain, and service CPU costs remain candidates for a controlled component comparison.
See [the retained CPU evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/selected-cpu.md) for exact values and limitations.

## Probe density

Two further captures compare intervals 1024 and 8192 with identical source and managed binary hashes.
All eight arms complete without invalid records or integrity errors.
The sparse capture uses 144.7 ms less selected CPU at zero speedup and 158.1 ms less at half speedup.
The sparse treatment records 32 selected pauses, compared with 189 in the dense treatment.

Probe density changes observation and payment scheduling as well as probe frequency.
These captures therefore do not isolate the cost of injected instructions or source draining.
The dense capture ran first, and physical CPU costs differ between captures.
The results do not establish a repeatable performance improvement.

The sparse sampled effective elapsed reduction is 29.7–30.2%, compared with 44.8% physical elapsed reduction.
Effective elapsed time subtracts the frozen increase in global delay from raw elapsed time.
Lower probe density does not resolve the prediction gap.
Pending-service CPU and source-drain CPU remain separate candidates for measurement at a fixed interval.
See [the density evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/selected-cpu-density.md) for both retained captures.

## Component observations

An opt-in component capture separates owner drains from service activities at interval 8192.
Its zero, half-speedup, and expected-invalidation cases pass with unchanged source inputs.
Treatment records 36.9 ms in drain CPU brackets, 12.9 ms in pending service, and 11.2 ms in receipt handling.
Both completed cases perform 100,000 explicit drains for 50,000 selected batches.

Each bracket adds two CPU clock reads. These observations can change sample timing and payment scheduling.
The totals do not measure component costs without the diagnostic.
They also do not prove that nearby drains are redundant, because samples can arrive between drains.
Pending service and receipt handling need separate inspection before changing their cadence.

See [the component reference](../Reference/cooperative-component-cpu.md) and [retained evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/component-cpu.md).

## Remaining gap in the longer workload

A 500,000-batch comparison records 141.5 ms more selected-thread CPU in treatment than at zero speedup.
Its sampled reduction is about 39.25%, against 52.93% physically.
Extra treatment work and sample attribution remain separate questions.

A fixed-record sensitivity calculation replaces sample credit with half of all measured selected CPU.
It preserves recorded durations, progress, and the global-delay contribution above sample credit.
This gives 46.86–46.87% reduction and still leaves about six percentage points of shortfall.
The substitution includes profiler and selected-payment CPU. It is not a replacement estimator or an accuracy bound.
It does not simulate how changed credit would affect execution.

The longer component capture fails the peer-debt bound after 192,529 batches.
It retains drain and service CPU observations, but provides no complete paired component result.
Diagnostic clock cost also remains in those observations.

A separate scratch candidate moves the controlled method's loop probe to entry and removes the repeated loop countdown.
Eight component pairs retain a median 11.26% elapsed reduction with the same callback count and full binding guard.
The comparison also changes callback placement, so it does not isolate one instruction's cost.
Its subsequent four-arm comparison completes but reports about 31.00% sampled reduction against 49.95% physically.
The separate runs cannot isolate a placement regression or improvement.
The candidate has not resolved prediction, despite the component speedup.
Entry-only probes cannot cover a long-running invocation. The normal countdown profiler remains in use.
See [the retained diagnostics and failure](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/prediction-gap-diagnostics.md).

## Upstream accounting

Linux Coz [subtracts the increase in global delay from elapsed time](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L359-L391).
Its [viewer divides duration by progress](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/viewer/ts/profile.ts#L860-L870).
CozNet uses the same algebra for its descriptive fixed-work comparison.
Summing all thread waits would double-count overlapping delay. Subtracting selected credit again would also change the estimator.

Coz [processes samples before the delay check](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L736-L757).
It [disables sampling during the actual wait branch](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L707-L727).
It does not generally subtract profiler processing CPU from elapsed time.
Thus, matching this rule alone does not remove intervention-dependent overhead.

The CPU observations remain separate from causal estimation.
Changing estimator inputs to force agreement would hide the uncertainty rather than measure it.

See [the field reference](../Reference/selected-cpu.md) and [nominal-event evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/nominal-event-credit.md).
