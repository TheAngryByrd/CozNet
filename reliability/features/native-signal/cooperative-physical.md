# Cooperative physical execution

Retained capture: `artifacts/cooperative-physical/20261016T020351651`.
The declared order is Windows baseline, Windows optimized, Linux baseline, then Linux optimized.
Each mode ran once with 50,000 selected batches, disabled tiering, disabled ReadyToRun, and disabled startup profiling.

| Platform | Mode | Exit | Frozen selected progress | Frozen peer progress | Runtime |
| --- | --- | --- | --- | --- | --- |
| Windows x64 | Baseline | 0 | 50,000 | 52,441 | .NET 10.0.12 |
| Windows x64 | Optimized | 0 | 50,000 | 26,757 | .NET 10.0.12 |
| Linux x64 | Baseline | 0 | 50,000 | 80,793 | .NET 10.0.0 |
| Linux x64 | Optimized | 0 | 50,000 | 53,539 | .NET 10.0.0 |

`AnalyzeCooperativeComparison.analyze_case` accepted every physical record.
The checks require matching checksum totals, fixed selected work, positive frozen peer progress, and no intervention accounting.
The workload file hashes in `plan.json` remained unchanged after all four runs.
`physical-analysis.json` retains the parsing results. Each platform has a separate outcome list and process logs.
The Linux runtime image is `sha256:3db72bd242b5f26a45d74c2d49a7654740e75c732654658177e52aa498645c14`.

## Boundary uncertainty

| Record | Start bracket | End bracket |
| --- | --- | --- |
| Windows baseline | 6,095,300 ns | 500 ns |
| Windows optimized | 7,330,900 ns | 1,100 ns |
| Linux baseline | 107,390,036 ns | 1,459 ns |
| Linux optimized | 90,219,974 ns | 1,427 ns |

The first boundary capture remains cold. First-call JIT and type loading are possible causes of the width, but remain unproven.
The comparison retains these brackets and uses their full uncertainty.
This width is an accepted limit for the first diagnostic matched block because that block makes no prediction-accuracy claim.
The current implementation does not warm boundary capture or impose a bracket-width gate.
The optimized route also changes the peer work completed before the selected loop ends.
The comparison uses fixed selected work. It does not claim equal peer work or calibrated local optimization.

These checks establish controlled physical-route execution on these platforms.
They do not establish equal cross-platform timing, source attribution, a 50% local optimization, or prediction accuracy.
The runtime versions differ. This capture is not the four-arm sampled/physical comparison.

See [the physical reference](../../../docsSrc/Reference/cooperative-physical-work.md)
and [the comparison contract](../../../docsSrc/Reference/cooperative-comparison.md).
