# Bound probe component measurement

The Linux diagnostic retains eight shuffled blocks of four stages in `artifacts/bound-probe-cost/20261015T190639232`.
The initial four-stage smoke capture also remains under `artifacts/bound-probe-cost-smoke/`.
All 32 measured cases passed. Input hashes remained unchanged.
The retained manifest records revision, image identity, binary hashes, batch count, and seed `20261015`.
The outcomes record the actual order and process exits. Raw records and logs remain available locally.
The capture includes driver source, project, runner, and the subsequent analyzer.
The analyzer independently checks counts, identities, checksums, cleanup, and complete block membership.

Each process loads the existing workload and calls `CpuWorkload.Run` through the same compiled delegate.
It performs one initial batch and 2,000 callback warmup batches before 50,000 measured batches.
Injected cases require 50,000 entry probes and 450,000 loop probes.
The lifetime stage calls `Execute` and `RequireOrigin`.
The idle-poll stage calls `PollFromProbe` and rejects non-idle outcomes.
No stage collects CPU samples or pays debt.

| Stage | Median ms | Minimum ms | Maximum ms | Calling-thread bytes |
| --- | ---: | ---: | ---: | ---: |
| baseline | 160.570 | 154.423 | 191.589 | 0 |
| probes | 180.592 | 178.689 | 217.348 | 0 |
| lifetime | 202.562 | 196.121 | 238.496 | 0 |
| idle-poll | 221.784 | 214.999 | 329.227 | 0 |

Within-block median differences between adjacent stages are 20.685, 20.132, and 18.874 ms.
Individual differences include negative values and large positive values.
No observation was removed.
The stages have different callbacks and diagnostic checks.
The differences do not isolate exact native or managed component costs.
The Linux library paths belong to this scratch diagnostic, not a shared platform contract.

The runtime implementation remains unchanged.
A cheaper native binding query remains a candidate that requires admission and concurrency proof before use.
These measurements do not establish prediction accuracy or general application overhead.

See [the explanation](../../../docsSrc/Explanations/bound-observer-overhead.md) and [observer evidence](fixed-work-observer.md).

## Full identity admission followed by current-state queries

The candidate capture is `artifacts/bound-lifetime-cost/20261015T191144500`.
The measured capture contains eight shuffled pairs with seed `20261015` and 50,000 batches per case.
All 16 processes passed. Hashes remained unchanged.
The manifest includes driver, project, runner, analyzer, and binary hashes.

The scratch candidate calls the existing full `RequireCurrent` once before constructing its timed guard.
Its predicate requires the same immutable binding object and calls `CozIsProbeBindingCurrent(generation, 2)`.
The existing `Execute` and `RequireOrigin` remain on the measured callback path.
After measurement, both stages load a duplicate selected module and require permanent lifetime failure.
All sixteen cases reached and rejected this invalidation.
The measurement does not invalidate concurrently with an operation or exercise an active native wait.

| Guard | Median ms | Minimum ms | Maximum ms | Calling-thread bytes |
| --- | ---: | ---: | ---: | ---: |
| Full identity on each check | 214.854 | 197.402 | 380.907 | 0 |
| Full admission, then current-state query | 201.412 | 190.615 | 259.487 | 0 |

The median within-block reduction is 15.615 ms across 500,000 probes, approximately 31 ns per probe.
Seven pairs favored the candidate. One pair favored full identity checks by 13.236 ms.
All timing outliers remain. The candidate has a different predicate and a binding-reference check.
The comparison does not isolate native export cost or predict savings in the sampled workload.

The direct replacement is invalid because the cheap query cannot distinguish altered module, function, or method fields.
A two-step admission design is plausible for the current single-generation, permanently invalidating native record.
Its full-workload benefit and concurrent admission behavior remain unverified.
The runtime change is deferred. Full identity checks remain in production.
