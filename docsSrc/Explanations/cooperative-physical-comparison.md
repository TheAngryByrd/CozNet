# Comparing cooperative work with a physical change

A physical comparison needs the same application work and worker roles as the sampled experiment.
The older timer route runs two workers with a sleeping controller.
The bound cooperative route uses the caller as the selected owner.
That difference prevents the timer route from serving as a matched cooperative baseline.

The cooperative physical route shares checksum arithmetic and progress publication with the bound sampled route.
It also uses the caller for selected work and a dedicated peer worker.
These shared parts reduce differences between the workloads.
They do not establish equal scheduling, instrumentation cost, or sample attribution.

The shared work checks each checksum before it publishes progress.
This changes per-batch costs in the sampled path.
Timing records from the previous implementation do not describe the same workload.

The planned comparison has four arms: physical baseline, physical optimization, zero virtual speedup, and 50% virtual speedup.
Each arm needs the same fixed selected batch count and frozen measurement boundaries.
The comparison must retain failed arms and protect input identity.
The matched runner has controlled checks for process failure and input integrity.
Its first reviewed live capture completes all four arms and remains unqualified.
The [comparison analyzer](../Reference/cooperative-comparison.md) has portable checks for parsing, failure retention, and descriptive bounds.
These checks do not replace a reviewed live capture.

The optimized arithmetic preserves the checksum.
It has not established an exact 50% reduction in selected-region time.
Local optimization calibration, endpoint uncertainty, source membership, and instrumentation attribution remain unresolved.
A completed physical run alone cannot establish prediction accuracy.

The first boundary capture remains cold.
The earlier physical-only capture has Linux start brackets of about 107 ms and 90 ms.
The matched block has physical start brackets of about 108.6 ms and 56.1 ms.
First-call JIT and type loading are possible causes, but the records do not prove the cause.
The first diagnostic matched block retains this uncertainty without adding a warmup or claiming an accuracy bound.
The first live sampled arms exercise the changed shared work and pass retained replay.
Earlier captures cannot serve as unchanged baselines for this integration.

See [the physical command reference](../Reference/cooperative-physical-work.md)
and [the measurement lifecycle](experiment-lifecycle.md).
