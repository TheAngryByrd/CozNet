# Eligibility epoch prefix evidence

The sample producer uses nominal periods and discards the first accepted sample in each eligibility epoch.
This rule remains unchanged. Native Coz has no matching exclusion rule, but does not prove an alternative counter-weighted estimator.
The Linux task-clock timer preserves its remaining period across disable and enable.
A nominal first period can therefore span enabled CPU on both sides of a payment pause.

`ReadDisabledCounters` validates the stopped event count.
`BoundCpuSession` keeps initial and current snapshots, replacing the current snapshot on each transition.
`CpuCounterTransition` has no CPU count. `BoundEpochSample` has only an epoch and sample.
The final evidence cannot reconstruct each disabled baseline from those fields.

The next diagnostic should retain the disabled reading before each enable and bind it to the resulting epoch.
It must keep source identity, native owner, drained old records, successful transitions, counter order, loss checks, and CLR lifetime validation.
The calculation is:

```text
enabledPrefixCpu = firstSample.CumulativeCpuNanoseconds - disabledBaseline.CpuNanoseconds
```

This is enabled CPU, not selected-method CPU.
A post-enable read is not a substitute: the first sample can already precede that read.
Filtered kernel samples can enlarge a visible sample's counter delta.
Any credit policy needs a separate attribution contract and validation of mixed nominal/counter weights.
Authoritative wall-time payment and all debt limits remain unchanged.

```mermaid
flowchart LR
    Disable --> Drain[Drain old epoch]
    Drain --> Baseline[Retain disabled count]
    Baseline --> Enable[Enable next epoch]
    Enable --> Sample[First owned sample]
    Sample --> Prefix[Enabled prefix diagnostic]
    Prefix --> Attribution[Separate attribution decision]
```

The [fixed-trace sensitivity](../../reliability/features/native-signal/epoch-credit-sensitivity.md) restores all 61 mapped baseline nominal periods hypothetically.
The extra 30.5 ms changes the calculated reduction to 9.36%–9.42%, versus approximately 50.5% physically.
This does not simulate changed runtime behavior or bound a counter-weighted estimator.
It rules out nominal baseline-credit arithmetic as a complete explanation of that retained gap.

See [counter attribution](../../docsSrc/Explanations/cpu-counter-attribution.md), [sample credits](sample-credits.md), and [the index](../lode-map.md).
