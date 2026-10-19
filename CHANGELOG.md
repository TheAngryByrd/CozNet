# Changelog

## [Unreleased]

### Added

- Experimental serial profiling with explicit CPU regions.
- Randomized paired experiments, raw observations, and bounded settings.
- Deterministic tests and a real CPU comparison sample.
- Lode Coding project knowledge.
- Experimental Linux x64 target-local profiler: `TargetLocalProfiler.runBound`, `CodeBinding`, `TargetBarrier`, `TargetQueue`, `TargetSemaphore`, and `TargetLock`, with an optional spin of up to 1 ms before a lock wait or a `TargetQueue` dequeue wait. `TargetQueue.SpunDequeues` counts the dequeues that take their item in a spin. Each causal profile experiment keeps its ledger failures, the journal events before each failure (`TargetCausalContextEvent`) with a summary for each worker (`TargetCausalWorkerContext`), and its GC collection count and pause.
- `TargetCausalDesign.tryCreate` and `TargetCausalProfile.run` for seeded causal profiles with `EndToEnd`, `Throughput`, and `Latency` progress points. `TargetProgressCounter.Begin` and `End` mark a transaction, `TargetProgressCounter.Transactions` returns their begin and end times, and a `Latency` prediction subtracts the global delay added during each transaction.

### Changed

- `TargetCausalDesign.tryCreate` takes a `rotation` after `seed`. The seed fixes one base order, and each block rotates it. An invalid rotation returns `InvalidRotation`.
- `TargetProgressPoint` has a `Latency` case, so an exhaustive match on it must handle `Latency`. `TargetCausalExperiment` holds `Visits`, `Arrivals`, `LatencyNs`, `VirtualLatencyNs`, `NegativeTransactions`, `Threads`, `Failures`, `Collections`, and `GcPauseNs`, each `TargetCausalFailure` holds `ContextEvents`, `Context`, and `Workers`, and `TargetCausalProfileResult` holds `Progress` and `Warmup`. Code that constructs these records must set the new fields.
