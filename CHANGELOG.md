# Changelog

## [Unreleased]

### Added

- Experimental serial profiling with explicit CPU regions.
- Randomized paired experiments, raw observations, and bounded settings.
- Deterministic tests and a real CPU comparison sample.
- Lode Coding project knowledge.
- Experimental Linux x64 target-local profiler: `TargetLocalProfiler.runBound`, `CodeBinding`, `TargetBarrier`, `TargetQueue`, `TargetSemaphore`, and `TargetLock`, with an optional spin of up to 1 ms before a lock wait. Each causal profile experiment keeps its ledger failures and its GC collection count and pause.
- `TargetCausalDesign.tryCreate` and `TargetCausalProfile.run` for seeded causal profiles with `EndToEnd` and `Throughput` progress points.

### Changed

- `TargetCausalDesign.tryCreate` takes a `rotation` after `seed`. The seed fixes one base order, and each block rotates it. An invalid rotation returns `InvalidRotation`.
- `TargetCausalExperiment` holds `Visits`, `Threads`, `Failures`, `Collections`, and `GcPauseNs`, and `TargetCausalProfileResult` holds `Progress` and `Warmup`. Code that constructs these records must set the new fields.
