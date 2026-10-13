# Profiler overhead in Coz accounting

Date: 2026-10-13.
Native revision: `256e3c389424d6a1691ee5d18b5ecb5a6c1001ec`.
This report reviews the implementation and the authors' paper. It performs no new measurements.

## Answer

Native Coz does not generally measure its sampling and settlement service time and subtract that time from experiment duration.
Its Linux correction subtracts the experiment's global delay increase.
It excludes setup before starting the experiment clock, compares against profiled zero-speedup baselines, and limits service overhead through sampling and batching. [C1][C2][P1]

These mechanisms do not establish a universal correction for the cost of injected .NET countdown gates.
Excluding gate samples from selected CPU does not remove gate execution from elapsed duration.
It can reduce virtual credit while leaving that duration cost intact.

## Source-backed accounting

Let `P` be the nominal sample period, `d` the virtual credit per selected sample, and `v=d/P` the requested fractional speedup.
The pinned source uses `P=1 ms` and batches ten samples for processing. [C2][C3]
On Linux, the processing timer uses `CLOCK_THREAD_CPUTIME_ID`. [C4]
This is a CPU-time service cadence, not a helper invocation at every application loop edge.

For thread `i`, let `L_i` be its local delay counter and `G` the global delay counter.
The Linux path implements these updates: [C5][C6]

```text
selected sample:       L_i := L_i + d
settlement, L_i > G:   G := G + (L_i - G)
settlement, L_i < G:   wait for G - L_i
                      L_i := L_i + measured wait duration
```

The measured wait belongs to an intentional virtual-speedup pause.
The sampler stops before that wait and restarts afterward. [C5]
The source does not surround all sample processing, source lookup, counter work, or marker work with a corresponding service-time credit.
`process_samples` adds selected-sample credit and then calls `add_delays`; it has no measured service-duration adjustment. [C6]

For an experiment, define:

```text
W = end timestamp - start timestamp
D = global delay at end - global delay at start
E = W - D                         Linux effective duration
q = E / completed progress        effective time per progress unit
r = 1 - q_treatment / q_zero      predicted elapsed reduction
```

The profiler records `E` and progress deltas.
The viewer computes throughput-point cost as `duration/delta` and compares it with the same selected location's zero-speedup value. [C1][C7][C10]
This `r` is an elapsed reduction per unit of progress, not the fractional throughput increase.

`D` is a global virtual-time adjustment.
It is not the sum of all threads' measured sleep durations or service durations.
Summing per-thread elapsed intervals would count overlapping work more than once.
The paper's local/global invariant and synchronization rules are essential to this distinction. [P1] (§3.4)

## What is outside the clock

The pinned implementation snapshots progress counters before recording `start_time`.
Its comment explicitly excludes variable setup overhead from the timed interval. [C1] (lines 294–324)
It records the end timestamp before writing experiment results and applies a cooloff between experiments. [C1] (lines 361–481)
These boundaries avoid charging work outside the interval to that experiment.
They do not erase service performed during the interval.

The pinned macOS path additionally subtracts a separately accumulated nanosleep overshoot counter.
That counter measures actual wait beyond the requested wait, not arbitrary profiler service. [C8]
Do not import it as a general overhead correction or assume its aggregation is suitable for the .NET workload.

Linux records actual wait in `L_i`, which can affect later settlement.
Its path does not apply the separate macOS overshoot subtraction. [C5]
An implementation must preserve one consistent delay-accounting policy rather than subtracting the same interval twice.

## What the paper assumes and measures

The paper uses sampling because pausing other threads at every execution of a selected line would create prohibitive overhead.
It batches ten samples because more frequent processing can distort execution. [P1] (§§3.1, 3.4)
Its pause reduction preserves equality between each thread's selected-sample credits plus paid pauses, using local and global counters. [P1] (§3.4.3)
This optimization reduces unnecessary delays. It does not estimate the cost of profiler instructions.

The paper requires a zero-speedup baseline for each selected line.
It states that this controls line-dependent overhead, including communication associated with frequently executed lines. [P1] (§2)
That comparison is important, but it is not an instruction to subtract an independently measured mean overhead from each observation.
Neither the paper's formula nor the inspected Linux implementation provides such a subtraction.

The evaluation measures four configurations: unprofiled, startup-only, sampling without delays, and full profiling.
It reports average overhead components of 2.6% startup, 4.8% sampling, and 10.2% delays, totaling 17.6%. [P1] (§4.4)
These are evaluation results for the paper's benchmarks, not constants used to correct experiment records.
The paper proposes reducing or amortizing overhead and collecting longer profiles when necessary.

Native Coz also examines caller addresses when matching a selected source line. [C9]
That inclusive attribution differs from CozNet's exact top-level method-symbol filter.
It is not an explicit measurement of service overhead and must not be copied as permission to credit all helper samples.

## Consequence for the current .NET result

The retained interval-1024 run reports 26.36% mean zero-speedup overhead against the physical baseline.
It predicts 44.99% reduction against a measured physical reduction of 49.87%.
The mean error is −4.87 percentage points, with a 2.07-point interval half-width. [L1]
This report does not isolate the cause of that error.

An illustrative serial model shows why an instrumented baseline does not necessarily cancel additive overhead.
Let original work cost `B`, removable work cost `X`, and unchanged instrumentation cost `H` per progress unit.
If the effective treatment removes only `X`, then:

```text
physical reduction       = X / B
profiled prediction      = X / (B + H)
                         = physical reduction / (1 + H/B)
```

This is an explanatory model, not a fitted correction for the concurrent workload.
If its assumptions held with 26.36% overhead and 49.87% physical reduction, it would predict approximately 39.47%, not the observed 44.99%.
The discrepancy confirms that those two aggregate measurements do not identify the complete bias mechanism.

Gate execution can enter both the elapsed denominator and the selected-sample numerator.
Removing its selected samples alone changes only the latter.
Helper work, unmapped code, cadence, wait overshoot, and scheduling can also differ between control and treatment.
Unmapped samples remain unknown; they do not automatically represent profiler work. [L1]

## Defensible next steps

1. Preserve `E=W-D` and the existing failure gate while investigating overhead.
2. Measure the same workload with no profiler, loaded profiler without probes, probes without collection, collection without intervention, and full intervention.
3. Preserve the same warmup, code version, progress definition, and measurement boundaries across those comparisons.
4. Measure service intervals diagnostically. Do not immediately add them to delay credit or subtract their sum from elapsed time.
5. Use several probe intervals and sample-processing cadences. Compare both zero-speedup cost and prediction error.
6. Keep known helper, mixed inline-gate, and unknown CPU attribution separate.
7. Validate any proposed correction against physical optimizations and concurrency workloads before changing the estimator.

A portable estimator can exclude setup by moving consistent experiment boundaries, as native Coz does.
It can also exclude known deliberate waits through the established global-delay model.
The reviewed sources do not justify a portable rule that subtracts arbitrary per-thread service time from a concurrent experiment clock.

In a strictly serial workload, directly measured, nonoverlapping service intervals can support a separate sensitivity analysis.
That still requires evidence that service does not change the remaining work or its sampling distribution.
It is not a correction for the present concurrent workload without further proof.

Do not add profiler CPU duration to selected-sample credit.
That would model optimizing the profiler rather than the selected application work.
Do not subtract an average 26.36% overhead or rescale every prediction by that factor.
Native Coz's implementation and paper provide no such accounting rule.

## Sources

[L1]: ../../reliability/features/linux-perf/countdown-prediction.md
[P1]: https://arxiv.org/html/1608.03676v1
[C1]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L294-L481
[C2]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.h#L39-L47
[C3]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L600-L623
[C4]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/ccutil/timer.h#L17-L29
[C5]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L678-L734
[C6]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L736-L758
[C7]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/viewer/ts/profile.ts#L1106-L1153
[C8]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L361-L391
[C9]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/libcoz/profiler.cpp#L637-L674

[C10]: https://github.com/plasma-umass/coz/blob/256e3c389424d6a1691ee5d18b5ecb5a6c1001ec/viewer/ts/profile.ts#L860-L874
