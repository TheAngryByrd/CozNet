# Target-local causal profile

## Problem

Coz runs many experiments in one process. Each experiment selects one code region and one virtual speedup, and Coz then reports a causal profile.
CozNet ran one experiment for each process, with a method range and a speedup chosen before the run. The prediction suite checks one point of a profile.

## Decision

`TargetCausalProfile.run` in `src/CozNet/TargetCausalProfile.fs` runs a randomized experiment design in one process. It calls `TargetLocalProfiler.runBound` for each experiment, so it requires Linux x64. The design and prediction code are portable.
- Each block runs one baseline experiment at speedup 0, and one experiment for each candidate method and each speedup. The order in the block is a seeded random permutation.
- Each experiment runs the whole fork/join workload. The progress point is the end of the workload, so the result of an experiment is its effective duration.
- The prediction for a candidate, a speedup, and a block is `1 - E / E0`. E is the effective duration of the experiment, and E0 is the effective duration of the baseline of the same block.
- A failed experiment or a failed baseline makes the prediction of that block invalid. The profile keeps every experiment, with its error, and counts invalid blocks. No result is dropped.
  An error names the exception type, or each condition that rejected the run: binding, replay, ledger, close, signal, epoch, loss, quiescence, and handler errors.
- Before each experiment, the driver runs a full blocking collection outside the measured span. This reduces the garbage that earlier experiments carry into a later experiment.
  It does not prevent a collection inside an experiment. Finalizers that the collection queues can still run and allocate.
- `TargetCausalDesign.tryCreate` parses the design before any experiment. The candidates must be distinct bound symbols, and each must be eligible.
  Each eligible symbol must be bound. The speedups must be distinct and from 1 to 100. The replay plan must accept the period and the ranges.
  Invalid input returns a list of `TargetCausalDesignError` values. `TargetCausalProfile.run` takes only a parsed design.

Throughput progress points are not part of this change. [The throughput plan](target-local-throughput.md) adds them with one bound run for each fixed-length experiment.

## Qualification

The sample mode `causal-profile` runs one block with `Work.selected` and `Work.peer` as candidates and speedups 25, 50, and 75. The selected thread runs 6000 rounds and the peer 3000.
For each block, the runner runs the profile process and seven plain processes in a random order: the original workload, and one optimized workload for each candidate and speedup.
An optimized workload removes the speedup fraction of the rounds of the candidate thread. The actual gain of a point is `1 - optimized / original` in the same block.

For this workload, the fork/join model gives the expected profile. `Work.selected` gains 25%, 50%, and 50% at the three speedups. The plateau comes from the peer, which then sets the end time.
`Work.peer` gains 0% at each speedup, because it is not on the critical path.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 37 and change none.

38. Each profile record holds seven experiments: one baseline at speedup 0, and one experiment for each candidate and speedup. The record holds the run order.
    Each record has the mode, seed, and rounds of its arm. Each passed experiment has `EffectiveNs` equal to `ElapsedNs` minus `GlobalNs`.
    Each prediction that the driver writes equals the prediction that the analyzer computes from the experiments.
39. Each of the six points has 5 valid blocks. Its median absolute difference between prediction and actual gain is at most 5 percentage points, and each block is within 10.
40. The analyzer writes `summary.json`, and the run passes only when criteria 38 and 39 hold and the source hashes are unchanged.
41. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local prediction](target-local-prediction.md) and [target-local status](target-local-status.md).
