# Binding transition comparison

Capture: `artifacts/binding-transition-cost/20261016T103215952`.
Source revision: `6b4d881`. This is an ignored scratch harness, not a runtime change.
The capture retains the harness, project, runner, input hashes, process logs, raw JSON, and `analysis.json`.
All 16 processes exited successfully. Input hashes remained unchanged.

Each fresh Linux process completed 2,000 warmup batches and 50,000 measured batches.
Probe interval was 1024. Each measured case executed 500,000 probes.
Tiered compilation and ReadyToRun were disabled. Pair order used seed 20261015.
Both imports read every identity field through the real lifetime guard.
Both rejected duplicate-module invalidation after measurement.
All cases allocated zero bytes on the calling thread during measurement.

| Block | Ordinary elapsed (ms) | Suppressed elapsed (ms) | Ordinary minus suppressed (ms) |
| --- | ---: | ---: | ---: |
| 0 | 197.640298 | 207.953193 | -10.312895 |
| 1 | 205.072598 | 199.295446 | 5.777152 |
| 2 | 197.378399 | 215.427809 | -18.049410 |
| 3 | 196.267377 | 191.000121 | 5.267256 |
| 4 | 223.935414 | 192.037133 | 31.898281 |
| 5 | 196.356261 | 196.052096 | 0.304165 |
| 6 | 201.298827 | 191.561275 | 9.737552 |
| 7 | 196.983921 | 193.838574 | 3.145347 |

The median paired difference is 4.2063015 ms.
The median paired percentage reduction is 2.1402338%.
These are descriptive statistics from eight pairs, not confidence bounds.
Six pairs ran the ordinary import first. Order and scheduling can affect the comparison.

This harness includes checksum arithmetic, injected probes, and lifetime checks.
It excludes live sampling, debt accounting, and active payment.
It does not isolate individual native-call duration or establish the documented sub-microsecond condition.
No forced-GC stress, unload check through the suppressed import, missing-library check, or Windows comparison ran.
No runtime import changed.

See [the decision and runtime contract](../../../docsSrc/Explanations/bound-observer-overhead.md#why-gc-transition-suppression-remains-deferred).
