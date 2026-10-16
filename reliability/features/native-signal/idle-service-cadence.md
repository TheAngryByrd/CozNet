# Idle service cadence

`CooperativeServiceCadence` reuses successful idle service for the exact immutable accounting state.
Pending requests and distinct state objects execute service. A failed executing call clears previous eligibility.
Skipped checks retain the timestamp of the latest actual debt observation.

## Focused checks

| Requirement | Check |
| --- | --- |
| Initial and incomplete calls execute. Only successful idle completion permits skips. | `OnlyCompletedIdleStateCanBeSkipped` |
| Pending requests execute. Failed execution and pending completion clear reuse. | `PendingAndFailedServiceClearReuse` |
| Equal-valued distinct states and new credit execute. Failed new-state service clears the old identity. | `DistinctStateRequiresService` |

All three checks assert exact execution and skip counts.
The host command exits zero:

```powershell
dotnet run --project tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj -c Release -- --verify-cooperative-managed
```

The Linux integration also runs these checks through `--verify-bound-cooperative-managed`.
All four managed groups pass.

## Linux integration

Capture: `artifacts/idle-service-payment/20261016T054616424-5a308ffc`.

```powershell
pwsh -NoProfile -File native/VerifyBoundSampledPayment.ps1 `
  -EvidenceDirectory artifacts/idle-service-payment -SelectedPayment -OwnerDrain `
  -DirectReceiptReady -FiniteMeasurement -SelectedCadence qualified-sample -SelectedBatches 50000
```

| Case | Checks | Executions | Skipped | Expected outcome |
| --- | ---: | ---: | ---: | --- |
| Zero speedup | 500350 | 350 | 500000 | Completed |
| 50-percent speedup | 500392 | 76523 | 423869 | Completed |
| Invalidate after selected payment | 1392 | 50 | 1342 | CLR lifetime rejection verified |

All three process exits are zero. Independent replay and outcome checks pass.
Input hashes remain unchanged. No failed case was repeated or omitted.
The invalidation case retains the expected failure and its earlier debt observation.
Debt, lease, child request, handler, and receipt bounds remain unchanged.
These counts establish live reuse, not reduced elapsed time or prediction accuracy.

## Prior physical comparison

The preceding value-record implementation has capture `artifacts/debt-value-comparison/20261016T053914349-91bba495b4cb4fc9b468d9259f0d2ca5`.
It predates idle service reuse.
All four workloads exit zero, with no invalid arms, integrity errors, runner errors, or changed inputs.
The comparison runner exits one because prediction remains unqualified.

| Arm | Lower cost, ns per progress | Upper cost, ns per progress |
| --- | ---: | ---: |
| Physical baseline | 3068.40300 | 3068.66822 |
| Physical optimized | 1580.99114 | 1581.27372 |
| Sampled zero | 8238.20134 | 8239.37710 |
| Sampled half | 8780.11188 | 8783.50270 |

Descriptive sampled elapsed reduction is −6.6192% to −6.5628%.
Physical elapsed reduction is 48.4659% to 48.4796%.
The ranges describe clock boundaries, not confidence or accuracy bounds.
Sample membership, attribution, and ledger residuals remain unqualified.
One declared block cannot isolate the value-record change from earlier unpaired captures.

## Accepted limits

The skip contract depends on immutable accounting and exclusive selected-owner updates.
A future time-dependent debt rule or mutable accounting field requires review of this contract before reuse remains valid.
Focused checks exercise the helper. The live capture exercises its workload call sites.
No focused check directly calls the local `Service` function.

Cadence counters are diagnostic. Offline replay does not validate them or require their presence.
Their values do not authorize credit, payment, or measurement qualification.
They include final draining after measurement freeze, so they cannot establish savings within the measured window.
Live integration does not isolate every timeout or observation-failure scenario.
Failure timestamps identify the latest actual debt observation, not the failure time.
Capture timestamps retain their separate failure-diagnostic meaning.

See [the reference](../../../docsSrc/Reference/bound-sampled-payment.md#idle-service-cadence)
and [the explanation](../../../docsSrc/Explanations/bound-observer-overhead.md).
