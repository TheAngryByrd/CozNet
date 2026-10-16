# CPU periods and missing tails

A sample has a nominal period, an instruction pointer, and an emission timestamp.
The period does not measure the exact CPU interval since the previous sample.
Timer delays and disabled intervals can separate the nominal period from elapsed CPU.

Cumulative event counts provide a separate observation.
Their difference measures the counter's increase between two reads.
Counter readings before and after each frozen endpoint give an inner and outer range for that increase.
This range preserves uncertainty about where the endpoint falls between the reads.

The counter only advances while its event is enabled.
Application code can execute while sampling is disabled.
Enable and disable records expose those intervals, but they do not measure their CPU cost.
Therefore, the enabled-event count range cannot bound all application CPU by itself.

An instruction pointer also does not classify every instruction executed since the previous sample.
A counter increase can include selected work, other work, and kernel execution.
Without additional attribution evidence, selected CPU can lie anywhere between zero and that entire increase.

CozNet now retains sample counts, endpoint readings, and transition journals.
The offline analyzer reconciles these records and reports counter prefixes, suffixes, and disabled elapsed intervals.
These observations do not establish a verified missing-tail bound.
Disabled elapsed time includes time when the target does not execute.
An accepted sample identifies one sampled instruction, not all work represented by its cumulative count.
Native journal suffixes require separate attribution.
Prediction accuracy remains unverified.

Nominal credit replay answers a different question: did the producer apply its declared sampling rule consistently?
It can reproduce every recorded credit while the sampled estimate remains biased.
Eligibility epochs describe when the producer permits credit. Replaying those labels does not prove that each transition was correct.
The counter parser and credit replay therefore remain separate diagnostics.

## First samples after a pause

Native Coz disables sampling during delay payment and enables it afterward.
Its Linux path credits a fixed delay per selected sample. It has no per-epoch first-sample exclusion.
See [sample processing and payment](https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L707-L757).

The inspected Linux task-clock implementation preserves the remaining timer period across stop and start.
Stopping updates the cumulative count. Starting resets the previous timestamp without resetting that count.
See [timer restart](https://github.com/microsoft/WSL2-Linux-Kernel/blob/linux-msft-wsl-5.15.153.1/kernel/events/core.c#L10808-L10868)
and [task-clock transitions](https://github.com/microsoft/WSL2-Linux-Kernel/blob/linux-msft-wsl-5.15.153.1/kernel/events/core.c#L10977-L10996).
These pinned sources do not identify the executing kernel binary.

A successful disabled count can establish a baseline before the next enable.
Subtracting it from the first sample count measures the enabled prefix, if source identity and epoch ownership hold.
An enabled prefix is the counted CPU from enable through that sample.
That prefix can include resume bookkeeping, other methods, and kernel work.
The sample IP cannot classify the whole prefix as selected-method CPU.
A post-enable counter read is unsuitable as the baseline because a sample can occur before that read.

CozNet checks disabled readings but does not retain each reading in its final transition journal.
The journal retains transition timestamps. Each source snapshot retains only its latest reading.
The next evidence change should retain the disabled baseline for each epoch and bind it to that epoch's samples.
It must preserve source identity, complete old-epoch draining, counter order, loss checks, and CLR lifetime checks.
This would permit prefix diagnostics. It would not by itself justify changing credit.

## Size of the discarded nominal credit

The retained cache comparison has 61 mapped treatment samples that establish epoch baselines.
Crediting their full nominal periods would add 30.5 ms at 50-percent virtual speedup.
Holding timestamps, progress, and the global-delay-minus-credit residual fixed gives a calculated reduction of 9.36%–9.42%.
The measured physical reduction is approximately 50.5%.
Thus, restoring this discarded nominal credit alone does not close the arithmetic gap in that trace.

This is a fixed-trace sensitivity calculation, not a prediction of changed runtime behavior.
Online credit changes can alter payments, scheduling, sample locations, and elapsed time.
The calculation also does not bound a different estimator that uses cumulative counter differences.
See [the retained calculation](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/epoch-credit-sensitivity.md).

See the [counter reference](../Reference/cpu-counter-evidence.md) and [clock alignment limits](sample-clock-alignment.md).
The [Linux perf implementation](https://github.com/torvalds/linux/blob/v6.6/kernel/events/core.c) defines sample output and task-clock counter updates.
