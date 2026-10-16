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
