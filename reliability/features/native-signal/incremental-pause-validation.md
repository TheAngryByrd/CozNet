# Incremental pause validation

The capture maintains sample totals at admission and compares them with each paused source batch.
Final completion retains the independent full-history validator.
Epoch snapshots, sample publication, credit, and payment bounds remain unchanged.
Setup exercises both validators on a private fixture before source creation.

## Initial long-run failure

Capture: `artifacts/selected-cpu-long/20261016T095653727-1e636ac4b1004eccb4c0b46983043920`.

This comparison increases fixed selected work from 50,000 to 500,000 batches.
It uses interval 8192, nominal-event credit, prepared receipts, owner/controller execution, and selected CPU capture.
Physical baseline, physical optimized, and sampled zero complete.
Treatment fails after 359,559 batches at 2,499,741 ns of peer debt.
The limit remains 2,000,000 ns. The failed record retains 1,747 accepted samples and 250 selected pauses.
Original inputs remain unchanged. The comparison has one invalid arm and no integrity or runner errors.
No prediction comparison is available from this capture.
It does not answer whether longer work reduces the relative effect of first-use costs.

## Isolated scaling

Baseline capture: `artifacts/validation-scaling/20261016T095945494`.
Aggregate capture: `artifacts/aggregate-cost/20261016T100544095`.

Each capture uses six fresh Linux processes with prefix counts 50, 250, 1,500, 1,500, 250, and 50.
Each process runs 10,000 repeated calls per measured stage after a separate first call.
All twelve processes exit zero. Both captures report unchanged inputs.
The harness constructs consistent batches from prefixes of the failed long record.
These records supply fixture values. The benchmark does not qualify that failed source or its experiment.

The aggregate capture measures the full validator and aggregate validator in each process.

| Samples | Full validation, µs/call | Aggregate validation, µs/call | Full bytes/call | Aggregate bytes/call |
| --- | ---: | ---: | ---: | ---: |
| 50 | 3.329–4.098 | 0.111–0.147 | 2648 | 336 |
| 250 | 15.013–15.026 | 0.097–0.107 | 4480 | 336 |
| 1500 | 91.817–99.732 | 0.109–0.115 | 8472 | 336 |

The earlier baseline capture measures 4.033–5.123, 15.184–15.291, and 91.642–93.913 µs for those prefixes.
Full validation also previously required a separate sample snapshot.
That snapshot allocates 24,104 bytes at 1,500 samples in the baseline capture.
The pause path no longer creates that snapshot for accounting validation.
It still creates the epoch snapshot required by the callback.

These figures exclude the new per-sample aggregate updates.
The harness retains a separate one-time aggregate construction observation, which includes first-use cost.
Fixed stage order and the small process count limit performance inference.
The result establishes lower repeated-check cost in this component experiment, not total-workload savings.

## Integration

Capture: `artifacts/aggregate-live/20261016T100713628-7f280bf9`.

All three declared outcomes pass at 50,000 selected batches: zero speedup, half speedup, and expected invalidation.
Original inputs remain unchanged. Replay exits zero.
Linux preflight passes the source, cooperative, pausable, and measurement-boundary groups.
The source and pausable groups also pass on Windows with controlled sources.

`BoundSampleAggregateChecks` covers empty and nonempty totals, duplicate pointers, mixed mapping, counter bounds, and every source-accounting field.
It also checks that rejected overflow and null admission preserve existing totals.
`PauseRejectsChangedAggregate` injects ten inconsistent batches through the actual owner collection.
Every case rejects the pause before its callback or source resume and disposes the source.
Existing epoch, final-drain, invalidation, and cleanup checks continue to pass.

The reviewed implementation limits aggregate creation, preparation, and updates to pausable captures.
`PauseAcceptsMatchingAggregate` supplies a positive control with the same overridden source batch as the rejection checks.
It verifies the callback prefix, resume, and final audit.
`FullValidatorParity` calls the existing full validator directly and compares acceptance and rejection with the aggregate validator.
Its fixture includes duplicate pointers, mixed symbols, absent counters, and a regressed counter.
Both validators preserve the same accounting rules. The admission path still rejects foreign symbols and absent or regressed counters.

Final-tree capture: `artifacts/aggregate-reviewed-live/20261016T101942535-8eb7ee48`.
All three expected outcomes and all four managed preflight groups pass with unchanged inputs.
Replay exits zero. The updated source and pausable groups also pass on Windows.

## Longer comparison after the change

Capture: `artifacts/aggregate-long-comparison/20261016T100825359-dd213b4939394b80b519f9040af49d5f`.

This capture precedes the review changes that restrict aggregate work to pausable captures and add the positive-control and parity checks.
The aggregate validator and final full-history audit are unchanged.

All four arms complete 500,000 selected batches with zero invalid arms and no integrity or runner errors.
The sampled effective elapsed reduction is 35.6900–35.7292%.
The physical elapsed reduction is 51.6309–51.6321%.
The descriptive shortfall remains 15.9017–15.9421 percentage points.
Effective elapsed time subtracts the frozen increase in global delay from raw elapsed time.

Treatment consumes 2,473.334800 ms of selected CPU, versus 2,259.310100 ms at zero speedup.
It records 357 selected pauses and 1,021,500,000 ns of credit.
Peak observed peer debt is 999,946 ns.
The runner exits 1 because prediction remains unqualified.
Boundary ownership, ledger residuals, source-tail membership, attribution, and local optimization calibration remain unresolved.

The earlier long treatment failed, so it supplies no complete paired performance result.
One successful long capture does not establish stable capacity or isolate the cause of the earlier failure.
The remaining prediction gap requires further work.

See [observer overhead](../../../docsSrc/Explanations/bound-observer-overhead.md) and [CPU interpretation](../../../docsSrc/Explanations/selected-cpu.md).
