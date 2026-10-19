# Changelog

## [Unreleased]

### Added

- Experimental serial profiling with explicit CPU regions.
- Randomized paired experiments, raw observations, and bounded settings.
- Deterministic tests and a real CPU comparison sample.
- Lode Coding project knowledge.
- Experimental Linux x64 target-local profiler: `TargetLocalProfiler.runBound`, `CodeBinding`, `TargetBarrier`, `TargetQueue`, `TargetSemaphore`, and `TargetLock`, with an optional spin of up to 1 ms before a lock wait.
- `TargetCausalDesign.tryCreate` and `TargetCausalProfile.run` for seeded causal profiles with `EndToEnd` and `Throughput` progress points.

### Changed

- `TargetCausalDesign.tryCreate` takes a `rotation` after `seed`. The seed fixes one base order, and each block rotates it. An invalid rotation returns `InvalidRotation`.
- `TargetCausalExperiment` holds `Visits` and `Threads`, and `TargetCausalProfileResult` holds `Progress` and `Warmup`. Code that constructs these records must set the new fields.
