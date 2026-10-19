# Target-local probe diagnostic

## Supervisor decision

The supervisor approved this diagnostic on 2026-10-19, after it read `lode/tmp/target-local-visit-drop-review.md`:
- Implement one instrumented diagnostic, review it, and run exactly one capture. It is a new diagnostic. It does not replace earlier evidence, and it does not claim that host load caused the earlier slowdown.
- Report continuous paired rates, ratios of CPU time to work, raw sample sizes, and the temporal association as description only. No causal label.
- Keep the same-item comparison with both retained runs, `20261019T213806586-069c2adf` and `20261019T222832093-272334f5`, where the items are comparable.
- No automatic retry, no substitution, and no second timing run. If a verified implementation fault prevents the intended capture, stop and report.
- The historical protocol, the prediction criteria, the capacity bounds, the estimator, the seed, the order, the work, the duration, and the failed captures do not change.

## Probe

- Mode `probe <out>` of the sample runs in its own container, with the same image, environment, and mounts as an arm. It runs before each arm and ends before the arm starts.
- Work: `Work.selected 40` on one thread, the work of one producer transaction. The probe runs 20 untimed warm-up samples, then 100 timed samples.
- Affinity: none. The arms of this diagnostic do not pin their workers, so the probe does not pin its thread.
- Each timed sample records its wall time from `Stopwatch` and its CPU time from `CLOCK_THREAD_CPUTIME_ID`, separately, and the CPU numbers from `sched_getcpu` before and after it.
- Checksum: every call of `Work.selected 40` returns the same value. The probe records the value of the first call and the number of calls that differ from it, which must be 0.
- The probe records `CLOCK_REALTIME`, `CLOCK_MONOTONIC`, and `CLOCK_BOOTTIME` at its start and at its end.
- The probe has no historical baseline. The retained runs have no probe.

## Brackets and guest counters

- The shell of each probe and each arm container writes a snapshot before and after its process: the UTC time from `date -u +%s%N`, `/proc/uptime`, `/proc/stat`, `/proc/pressure/cpu`, and `/proc/loadavg`.
- `/proc/uptime` gives `CLOCK_BOOTTIME` with 10 ms resolution. It is the bracket of an arm on the monotonic time line, and the probe relates it to `CLOCK_MONOTONIC`.
- A file that cannot be read is recorded as `unsupported`. The analysis reports a missing counter as missing, not as 0.
- A functional check in the runtime image, before the capture, showed that this WSL2 kernel has no `/proc/pressure/cpu`, so the pressure counter is missing. `/proc/stat` has a steal column. The check is not evidence, and its timings are not used.
- Guest counters cannot prove that the host had no contention. A probe runs only before its arm, so it supports only a temporal comparison with the next arm.
- Before each arm, the probe adds a container start, a .NET runtime start, and about 0.1 s of work. This can change the thermal and idle state of the CPUs, so the capture is not an exact repeat of the earlier runs.
- If a probe exits with a code other than 0, the capture stops before that arm and records the reason in the manifest. It does not retry. The partial capture is retained.

## Analysis

`native/AnalyzeTargetProbe.py` writes `probe-summary.json` in the run. The runner also runs the normal analyzer, so the label of criterion 137 is computed as before.
For each arm in run order, the summary gives:
- The probe: its sample count, its checksum result, the median wall and CPU time for each sample, the CPU time over the wall time, and the CPUs seen.
- The brackets of the probe and the arm, and the deltas of the guest counters around the arm: all CPU ticks, steal ticks, and the total of `some` CPU pressure.
- The visit rate of the arm and, for a profile, the visit rate and the CPU time for each transaction of each experiment.
- The ratios of these values to the same item in each retained run, including the plain warm-ups and the profile warm-up experiments, with their visits and measured durations.
- The realtime, monotonic, and boot clocks of the probe at its start and end.

It also gives, as description only:
- The spread of the probe: minimum, median, maximum, and coefficient of variation of the median wall time.
- The Spearman rank correlation, with its sample size and its count of distinct probes, between the probe wall time and the median visit-rate ratio of the timed items of the next arm to each retained run, for plain and profile arms separately. One arm gives one value.

No slow arm, no probe variation, or too few comparable items means that the diagnostic cannot resolve the cause.

## Acceptance criteria

These criteria are fixed before the capture. They add to criteria 1 to 144 and change none.

145. Unit checks of the probe analyzer cover a snapshot with and without pressure counters, a missing steal column reported as missing, a failed checksum, and the rank correlation.
146. The runner without `-Probe` sends the same commands to Docker as before. With `-Probe`, it writes to `artifacts/target-local-bottleneck-probe`.
147. On the reviewed tree, the capture runs exactly once. The capture is retained, whatever its outcome, with both earlier paired runs.
148. The report gives the label of criterion 137, the probe summary, the same-item ratios to both retained runs, and the limits of this plan. It uses no causal label.
149. After the commit, every source hash in the new manifest equals the SHA-256 of the committed blob.

See [target-local clipping export](target-local-clipping-export.md) and [target-local status](target-local-status.md).
