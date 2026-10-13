# Serial proportional-delay model

The engine implements a restricted serial experiment. It does not implement native Coz's synchronization-aware delay credits.

For requested region-time reduction `s`, define `r = s / (1 - s)`.
The control adds `r` times each region's elapsed time. The treatment exempts the target.
If all work lies in marked serial CPU regions, both runs share the same scale factor.
Then `1 - treatment / control` estimates the whole-program time reduction from making the target region `s` faster.

For two regions of 100 time units and `s = 0.5`, the control takes 400 units and the treatment takes 300.
The estimate is 25%. Actually halving one region reduces the original 200 units to 150, also 25%.

Unmarked work breaks the common scale factor. Waiting, concurrent work, runtime pauses, and spinning also change the relationship.
Use repeatable synchronous CPU work and inspect raw observations.
A seed reproduces order under the same runtime implementation. It does not reproduce scheduling or processor state.

Each block contains a control and treatment for every speedup. The runner shuffles the complete block.
Warmups have no injected delay and do not enter results. Every measured run must record equal positive progress.
Sessions close after success or failure. Nested regions, cross-thread use, and progress inside a region invalidate the session.
Catching a rejected operation does not make a measurement valid.

The library reports paired median reductions and observed extrema. It does not report confidence intervals or remove samples.
The sample records GC counts, marked coverage, execution order, actual delays, runtime, processor count, and seeds.
A failed two-percentage-point check is evidence to investigate, not a reason to relax the threshold.

See [research](../Research/causal-profiling-dotnet.md), [terms](../../terminology.md), and [the roadmap](../../lode/plans/roadmap.md).
