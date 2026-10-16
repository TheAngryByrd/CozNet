# Comparing cooperative work with a physical change

## Result at probe interval 8192

The four-arm runner accepts `-ProbeInterval 8192` as an opt-in setting. Its default remains 1024.
The first 8192 block completes all four arms with unchanged inputs and no invalid records.
Both sampled records match the declared interval.

The sampled elapsed reduction is −14.4322% to −13.9043%, compared with a physical reduction of 46.5633% to 46.5761%.
The sampled treatment is slower in this block. Lower probe density has not resolved the mismatch.
Separate earlier captures cannot isolate the performance effect of changing the interval.

Treatment retains a pending peer request at measurement freeze.
The comparison remains unqualified for native ownership, ledger residuals, source membership, and instrumentation attribution.
Its ranges describe clock boundaries, not accuracy bounds.
See [the retained result](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/sparse-probe-comparison.md)
and [the interval contract](../Reference/cooperative-comparison.md#probe-interval-contract).

## Shared work and measurement

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

The comparison has four arms: physical baseline, physical optimization, zero virtual speedup, and 50% virtual speedup.
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

The historical first boundary capture was cold.
The earlier physical-only capture has Linux start brackets of about 107 ms and 90 ms.
The matched block has physical start brackets of about 108.6 ms and 56.1 ms.
First-call JIT and type loading are possible causes, but the records do not prove the cause.
The first diagnostic matched block retains this uncertainty without adding a warmup or claiming an accuracy bound.
The first live sampled arms exercise the changed shared work and pass retained replay.
Earlier captures cannot serve as unchanged baselines for this integration.

See [the physical command reference](../Reference/cooperative-physical-work.md)
and [the measurement lifecycle](experiment-lifecycle.md).


## Preparing the measurement path

The physical and finite sampled routes now capture a separate preparation window before the real start.
This exercises capture and validation without substituting preparation progress or duration into the estimator.
The sampled route prepares before source creation.
Preparation can change pre-measurement scheduling and cache state. It does not make measurement free of observer cost.

A final-code comparison completes all four arms with unchanged inputs.
Its actual start brackets are approximately 12–43 microseconds, while separate preparation start brackets contain milliseconds of setup cost.
The sampled reduction remains approximately 8.7%, against a physical reduction of approximately 51.3%.
Thus, narrower boundary uncertainty does not resolve the observed prediction mismatch.
The run remains unqualified for source membership, instrumentation attribution, and ledger residuals.

The earlier interleaved calibration uses unchanged arithmetic source, but a different measurement loop.
Its clock and overhead assumptions do not automatically qualify this comparison.
Further work must address profiler overhead and sampled attribution before making an accuracy claim.
See [the retained preparation evidence](../../reliability/features/native-signal/measurement-preparation.md)
and [the capture contract](../Reference/measurement-boundaries.md).

## Comparison after idle service reuse

The idle service path now skips repeated checks for the same successfully checked accounting state when no peer request is pending.
A subsequent four-arm block completes with unchanged inputs and no invalid arms.
The sampled elapsed reduction is −5.1888% to −5.1302%, while the physical reduction is 49.7434% to 49.7670%.
The sampled treatment is slower in this block, despite the physical improvement.

Zero speedup skips 500,000 of 500,357 service checks. Treatment skips 406,599 of 500,400 checks.
These lifetime counters include final draining outside the frozen window.
They establish reuse but cannot measure its elapsed-time benefit.
Earlier blocks ran separately, so their timing differences do not isolate the implementation change.

The mismatch remains after idle reuse. Source membership, instrumentation attribution, and treatment ledger residuals remain unqualified.
The ranges describe clock boundaries, not confidence intervals or accuracy bounds.
Owner draining and sample publication still precede the service guard and require separate cost measurements.
See [the retained comparison](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/idle-service-cadence.md#four-arm-comparison-with-idle-reuse)
and [the observer explanation](bound-observer-overhead.md).
