# Target-local candidate discovery and its limits

Candidate discovery tells you which methods a run sampled most, so that you can choose candidates for a causal profile. This page explains what the ranking means, why it is limited to methods, and what it cannot show.

## A ranking of sampled methods

Coz finds its experiment lines from samples. Between experiments, the first sample in known code chooses the next line, so a line is chosen in proportion to its samples.

CozNet ranks instead of choosing at random. A run with no added delay records the address of each sample. The ranking counts the samples of each method range in the perf map, and the balanced design of a causal profile then runs the methods that you choose.

The ranking is a ranking of methods, not a source-line profile:
- The target-local path knows only the native range of each method, from the perf map. It has no map from native addresses to IL offsets, so it cannot place a sample on a line.
- Each sample counts for the method that holds the interrupted address. CozNet records no callchain, so a sample in a callee does not count for its caller. Coz uses the callchain.
- The source anchor of a method is the first sequence point of the method that is not hidden. It tells you where the method is. It does not tell you which line the samples hit.

## What the live workflow check shows

A second live check ran the library workflow once, with the versioned evidence. Both fixture methods were ranked inside the eligible binding, anchored, and used as candidates of a valid design that was not run.
- One of 1536 samples was unmapped, and the other 1535 were in scope. So the check reached two of the four classes.
- The check had no method outside the eligible binding, no stale map, and no missing anchor. Those cases are covered only by the deterministic checks.
- A captured map shows only that the profiled process read it. It does not show that the run and the map belong together in time, or that a range held the same code for the whole run.

## The workflow and its preconditions

`TargetDiscoveryWorkflow` lets a program use discovery without the sample code. It checks what its inputs can show, and it states the rest as limitations.

It checks that the run completed and added no delay, and that the eligible binding agrees with the map. It cannot check that the run used speedup 0 or used that binding, because the run result records neither.
A captured map shows only that the current process read it. It does not show that the run came from that process, or that the run came before the read. So each outcome states these points as limitations, and it has no flag that calls the result verified.

An outcome keeps its own copy of the map. A design from an outcome uses the ranges of that map, so a candidate of one map cannot be bound again over another map.

## Candidates and payment methods

A discovered method is a candidate. A payment-eligible method is a method where the profiler can pay delay. The workflow keeps the two apart.
A discovered method outside the eligible binding stays a description. It enters a design only when the caller adds it to the eligible list. Then the eligible list is bound again over the map.
A later run checks its binding against its own map, by symbol and range. That check cannot show that a range still holds the same code, because the map records no code identity, and an address can be reused.

## Why some samples are not candidates

A candidate must be a method that `CodeBinding` can bind to one range. So each sample is put in one of four classes, and the counts add up:
- **Unmapped**: no perf-map entry holds the address, for example code in the runtime.
- **Not bindable**: the entry is not a method, it is not optimized code, or its name does not identify one range.
- **Out of scope**: you did not include the method, you excluded it, or it is a method of the profiler assembly `CozNet`.
- **In scope**: the method is a candidate.

The perf map can give one name to two code versions. A generic method compiled for `int` and for `string` has the same label, for example `PerfProbe.Shapes::Gen(!!0)`. CozNet does not join the samples of the two versions under one name. It marks them as not bindable.

## Why the symbol formats are limited

A canonical symbol, such as `CozNet.TargetLocal.Work::selected(uint64)`, must be equal to the name in the perf map. The runtime writes these names, and CozNet can only copy the forms that it has seen.
A .NET 10 runtime gave the forms for primitive types, `string`, `object`, one-dimensional arrays, references, pointers, nested types, and instance methods. `TargetSymbol.tryFormat` supports only those forms.
For generic methods, generic types, other value types and classes, and arrays of more than one dimension, it gives an error. It does not guess a name.

## Why a PDB must match

A PDB next to an assembly can be from an earlier build. CozNet uses an external PDB only when its id equals the id in the assembly. A PDB with the right name and the wrong id gives an error, not a wrong line. An embedded portable PDB always belongs to its assembly, so CozNet uses it first.

## What the checks show

The discovery code is portable managed code with no native profiler calls. The source anchors read the assembly and PDB files. Its checks use fixed inputs: perf-map labels from a real runtime, hand-built samples, and the PDBs of the test assemblies. They run on every platform, with two exceptions. The check of a locked file runs only on Windows, and the check that capture is unsupported is skipped on Linux.
The workflow checks use synthetic snapshots with labels that start with `synthetic:`. These checks do not show that the live Linux backend gives complete samples or a perf map that matches the run. `rank` checks that a run completed and added no delay. It cannot check that the perf map came from the same process after the run.

## What the live check shows

One live check ran on WSL2 Linux 5.15.153.1. The process ranked its own samples and read its own perf map before it exited.
- All 1547 samples were in scope, and both fixture methods were ranked: `Work.selected` with 1038 samples and `Work.peer` with 509.
- The anchors were `Work.fs` lines 7 and 17, and the design was valid.

That check ran the sample code before the workflow existed. So it does not validate the workflow. It shows that the live path of that sample code worked, and it shows less than it seems:
- Each worker ran one leaf method, so no sample was unmapped, not bindable, or out of scope. Those classes are covered only by the deterministic checks.
- The workload had no other method to find. So the check does not show how discovery separates many methods in a larger program.
- The run still needed a caller-supplied scope and eligible binding. It is discovery inside a scope, not automatic instrumentation of any application.

Discovery does not change the profiler, the estimators, or any qualification result. The target-local profiler stays experimental. See [target-local support status](target-local-support.md).

See [the API reference](../Reference/target-local-api.md#candidate-discovery).
