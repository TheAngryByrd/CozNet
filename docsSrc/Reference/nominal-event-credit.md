# Nominal-event credit

This internal estimator supports the controlled bound selected-payment workload.
It does not change the public serial API.
Its F# arithmetic and managed policy parser are portable. The current live source uses Linux perf.

## Activation and evidence

| Setting | Value |
| --- | --- |
| Runner switch | `-NominalSampleCredit` |
| Environment | `COZNET_SAMPLE_CREDIT=nominal-event` |
| Default environment policy | Absent or `interval` |
| Nominal record policy | `sampleCreditPolicy="nominal-event-v1"` |
| Interval record policy | `sampleCreditPolicy="same-epoch-interval-v1"` |
| Nominal record period | `nominalSamplePeriodNanoseconds=1000000` |

Other environment values reject. The bound runner requires selected payment for nominal credit.
Interval records contain a null nominal period.
Legacy records without policy metadata retain interval semantics.
The runner requires result policy metadata that matches its requested mode.

The four-arm runner accepts the same switch.
New plans bind `sampleCreditPolicy` with `sampleCreditEvidenceVersion=1`.
Both sampled arms require matching policy metadata and full replay.
Missing or mismatched policy metadata invalidates that arm and suppresses the aggregate comparison.
Legacy plans permit only interval credit. Physical arms require no credit-policy metadata.

## Internal model

`NominalSampleCreditStream.tryCreate` parses frequency, speedup from zero through 100, and a fixed positive nominal period.
`NominalSampleObservation.tryCreate` requires positive sequence, epoch, and period values.
Observations carry stream identity, thread state, location, and nominal weight. They contain no cumulative CPU value.

Each runnable selected event earns `period × frequency × percent / 100000000000` ledger ticks.
The stream retains the integer division remainder across observations and epochs.
Unmapped, other-location, and non-runnable observations earn zero and preserve the remainder.
An accepted observation consumes one sequence number.

Foreign streams, sequence gaps, duplicates, decreasing epochs, changed periods, and credit overflow reject without changing accepted state.
Epoch jumps can represent empty source epochs. The producer must verify the source transition journal separately.
Contiguous admitted sequence numbers do not replace source loss checks.

`SignalAccounting.registerNominal` creates a nominal stream.
`SignalAccounting.observeNominal` applies its credit through the same atomic ledger path as interval observations.
Wrong-estimator observation APIs reject. Pending intervention and required-epoch checks remain active.
Existing `register` and `observe` APIs keep interval semantics.

## Cadence

Qualified-sample cadence can consume one permission after the first current-epoch nominal event, including at zero speedup.
One event cannot authorize repeated checks. Resume clears old permission.
Interval cadence still requires two current-epoch observations.
The `Admission.Qualified` field identifies readiness under the declared credit policy.
It does not establish physical CPU attribution.

See [the explanation](../Explanations/nominal-event-credit.md) for estimator limits.
