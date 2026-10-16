# Bound sample membership evidence

Input capture: `artifacts/bound-sample-clock/20261016T112911781-1e9fd18d`.
Reports: `artifacts/bound-membership/20261016T115332908`.
The analyzer replays each case before classifying its source timestamps.
All three comparison domains pass. The expected invalidation case retains `completed=false` and `passed=true`.

| Case | Before | Inside | After | Ambiguous | Inside but unaccepted | Inside recorded credit |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Zero speedup | 2 | 68 | 2 | 0 | 1 | 0 ns |
| 50% speedup | 1 | 74 | 1 | 0 | 1 | 11,500,000 ns |
| Expected invalidation | 2 | 54 | 0 | 0 | 0 | 500,000 ns |

Each before-start sample belongs to the frozen accepted prefix.
All before-start samples and both inside-but-unaccepted samples are unmapped. They carry zero recorded credit.
All recorded selected credit lies inside the observed window.
The treatment contains 29 selected observations with 29,000,000 ns of nominal weight.
Its recorded 11,500,000 ns credit follows the same-epoch interval policy, including uncredited epoch baselines.
Nominal sample weights are not measured selected CPU.

These results show a source-time mismatch with frozen acceptance, but no retained selected credit outside the window in this capture.
They do not explain the earlier matched prediction gap.
The capture is a short interval-1024 diagnostic, not the earlier interval-8192 four-arm comparison.
Unreceived observations and exact CPU attribution remain unknown.
No payment, credit, timing bound, or qualification rule changes.

The 14 deterministic checks cover half-open boundaries, ambiguity, accepted outside credit, unaccepted selected tails, failed workloads, malformed input, and provenance.
Zero-frequency arithmetic failures retain rejection reports.
The command refuses an existing output path and leaves input bytes unchanged.
No new timing run was needed for this offline analysis.

See [the reference](../../../docsSrc/Reference/bound-sample-membership.md) and [the upstream comparison](../../../docsSrc/Research/sample-boundary-semantics.md).
