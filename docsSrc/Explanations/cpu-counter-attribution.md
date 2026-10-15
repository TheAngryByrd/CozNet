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

See the [counter reference](../Reference/cpu-counter-evidence.md) and [clock alignment limits](sample-clock-alignment.md).
The [Linux perf implementation](https://github.com/torvalds/linux/blob/v6.6/kernel/events/core.c) defines sample output and task-clock counter updates.
