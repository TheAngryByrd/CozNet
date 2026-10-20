# Target-local candidate discovery and its limits

Candidate discovery tells you which methods a run sampled most, so that you can choose candidates for a causal profile. This page explains what the ranking means, why it is limited to methods, and what it cannot show.

## A ranking of sampled methods

Coz finds its experiment lines from samples. Between experiments, the first sample in known code chooses the next line, so a line is chosen in proportion to its samples.

CozNet ranks instead of choosing at random. A run with no added delay records the address of each sample. The ranking counts the samples of each method range in the perf map, and the balanced design of a causal profile then runs the methods that you choose.

The ranking is a ranking of methods, not a source-line profile:
- The target-local path knows only the native range of each method, from the perf map. It has no map from native addresses to IL offsets, so it cannot place a sample on a line.
- Each sample counts for the method that holds the interrupted address. CozNet records no callchain, so a sample in a callee does not count for its caller. Coz uses the callchain.
- The source anchor of a method is the first sequence point of the method that is not hidden. It tells you where the method is. It does not tell you which line the samples hit.

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

The discovery code is portable managed code with no native profiler calls. The source anchors read the assembly and PDB files. Its checks use fixed inputs: perf-map labels from a real runtime, hand-built samples, and the PDBs of the test assemblies. They run on every platform, except one check of a locked file, which runs only on Windows.
These checks do not show that the live Linux backend gives complete samples or a perf map that matches the run. No live run has used discovery. `rank` checks that a run completed and added no delay. It cannot check that the perf map came from the same process after the run.

Discovery does not change the profiler, the estimators, or any qualification result. The target-local profiler stays experimental. See [target-local support status](target-local-support.md).

See [the API reference](../Reference/target-local-api.md#candidate-discovery).
