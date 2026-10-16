# Bound selected cadence comparison

The fixed-work diagnostic compares continuous and sample-qualified selected checks.
It remains internal. It does not extend the public serial engine.

## Commands

`--sampled-bound-selected-cadence <path> <percent> <library> <cadence> <batches>` runs fixed selected work.
`--invalidate-sampled-bound-selected-cadence <path> <library> <cadence> <batches>` invalidates the binding after selected payment.

`percent` accepts `0` or `50`.
`cadence` accepts `continuous` or `qualified-sample`.
`batches` accepts a positive decimal integer through `Int32.MaxValue`, without signs or whitespace.
The external watchdog still limits process duration.
The comparison queues the peer loop asynchronously on its owner thread.
The five-second command timeout does not limit that loop's total lifetime.
Peer shutdown and owner join retain their five-second bounds.

## Check admission

Both modes publish available original-epoch samples before a selected loop-probe check.
They service the peer before deciding whether to pay selected debt.
Continuous mode permits every check.
With interval credit, qualified mode requires two observations in the current eligibility epoch.
With [nominal-event credit](nominal-event-credit.md), one current-epoch event permits one check, including at zero speedup.
Qualification does not require selected-method attribution or nonzero credit.
A check consumes its permission even when selected debt is zero.
Sampling resume advances the epoch and discards permission from the final drain.
The legacy duration-based commands retain their previous admission path.

The [selected-payment bounds](bound-sampled-payment.md) remain unchanged.
Skipped selected checks do not forgive debt or suppress peer service and debt observations.

## Schema 4

The comparison retains schema 3 payment, pause, source, and failure fields.
It adds these fields:

| Field | Meaning |
| --- | --- |
| `selectedCadence` | `Continuous` or `QualifiedSample` |
| `requestedBatches` | Parsed fixed selected-work count |
| `selectedChecks` | Number of admitted checks, including checks without payment |
| `selectedWorkStarted`, `selectedWorkFinished` | Managed timestamps around selected work, before source completion and final peer drain |
| `pauses[].Admission.SampleCount` | Processed sample prefix at pause admission |
| `pauses[].Admission.Epoch` | Source eligibility epoch at pause admission |
| `pauses[].Admission.Qualified` | Whether unconsumed permission under the declared credit policy authorized that check |

The existing `timestampFrequency` defines the managed timestamp units.
A failed run can lack the work endpoint.
Replay requires the requested batch count and matching checksum for completed runs.
Every qualified pause needs two original-epoch samples for interval credit, or one for nominal-event credit, after the preceding final drain.
Replay does not reconstruct checks that caused no pause.
Schema 3 remains valid for retained duration-based runs.
The runner also checks the requested cadence and batch count against each result.
A missing, malformed, or mismatched result fails its outcome, even if the record contains `passed: true`.
A nonzero process exit also fails the outcome.

## Evidence limits

Fixed selected work does not fix peer progress or isolate each source of observer cost.
Both comparison modes add publication and service work at selected loop probes.
Their continuous control is distinct from the older continuous diagnostic.
Fewer pauses alone do not establish correct attribution or prediction accuracy.
The first comparison retains continuous failures caused by the synchronous worker's five-second loop timeout.
Its qualified treatments complete, but those results do not form a completed fixed-work comparison.
The revised comparison completes both qualified treatments and one continuous treatment.
The other continuous treatment rejects a child that exceeds the 5 ms handler bound.
The overall comparison remains failed.
See [the retained evidence](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/bound-selected-cadence.md).

## Optional pause timing

`VerifyBoundSampledPayment.ps1 -SelectedPayment -PauseTiming` sets `COZNET_PAUSE_TIMING=1` in each child.
`0` or an absent variable disables timing. Other values reject the workload.
Timing requires selected payment. It does not change sample eligibility or payment bounds.

Schema 3 and 4 records include `pauseTimingEnabled` and optional `pauses[].Timing`.
Legacy records without these fields remain valid.
Every enabled pause owns a fresh timing record. All values use the record's `timestampFrequency`.

| Timing field | Completed boundary |
| --- | --- |
| `Started` | Collection accepted the excluded-operation request, before its first lifetime check |
| `ProducerStopped` | Native producer stop returned and collection recorded the disabled state |
| `DisabledCountersAccepted` | Disabled counter read and session validation returned |
| `DrainAcknowledged` | Collector acknowledged its final drain |
| `BatchValidated` | Frozen sample batch validation returned |
| `SnapshotReady` | Immutable epoch snapshot was constructed |
| `SamplesPublished` | Workload published drained samples and their accounting effects |
| `CallbackReturned` | Excluded workload callback returned |
| `SourceEnabled` | Source enable returned |
| `EnabledCountersAccepted` | Enabled counter read and session validation returned |
| `CollectorResumed` | Collector resume returned |
| `Completed` | Final guarded lifetime check returned |

Unreached boundaries remain null after failure.
`SamplesPublished` belongs to the sampled workload callback. Other users of collection timing can leave it null.
The sampled-workload analyzer requires reached phases to form an ordered prefix.
It rejects enabled timing without records, timing while disabled, and resumed pauses with incomplete timing.

Clock reads and recording add observer cost.
Phase duration includes execution and descheduling. It does not identify JIT or CPU ownership.
Native receipt clocks remain separate from these managed timestamps.
