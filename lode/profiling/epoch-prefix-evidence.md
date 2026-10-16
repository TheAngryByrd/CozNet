# Eligibility epoch prefix evidence

The default interval producer uses nominal periods and discards the first accepted sample in each eligibility epoch.
The explicit [nominal-event estimator](nominal-event-credit.md) credits every admitted selected event, matching native Coz's statistical event rule.
Neither policy proves exact selected CPU or an alternative counter-weighted estimator.
The Linux task-clock timer preserves its remaining period across disable and enable.
A nominal first period can therefore span enabled CPU on both sides of a payment pause.

`ReadDisabledCounters` validates the stopped event count.
`BoundCpuSession` keeps initial and current snapshots, replacing the current snapshot on each transition.
`CpuCounterTransition` has no CPU count. `BoundEpochSample` has only an epoch and sample.
Those fields alone cannot reconstruct each disabled baseline.
`BoundCpuEpochStart.Parse` now retains the disabled reading and successful enable transition from a validated session advance.
The pausable collector stores each start before resuming its drain.
Successful collection completion derives immutable `EpochPrefixes` with the first source sample index and enabled CPU delta.
Empty epochs retain their start with null sample and delta fields.
The sampled workload emits `sourceEpochPrefixes`; the collection workload emits `epochPrefixes`.
Failed collection emits no validated prefix list.
Inconsistent prefix evidence rejects `Complete()` after source cleanup. This is a consistency gate, not a credit-policy change.

Prefix parsing checks source identity, epoch order, enable records, and sample counts against adjacent disabled baselines.
Collection completion retains native owner, drained old records, successful transitions, loss checks, and CLR lifetime validation.
The calculation is:

```text
enabledPrefixCpu = firstSample.CumulativeCpuNanoseconds - disabledBaseline.CpuNanoseconds
```

This is enabled CPU, not selected-method CPU.
A post-enable read is not a substitute: the first sample can already precede that read.
Filtered kernel samples can enlarge a visible sample's counter delta.
Any credit policy needs a separate attribution contract and validation of mixed nominal/counter weights.
Authoritative wall-time payment and all debt limits remain unchanged.
The [live evidence](../../reliability/features/native-signal/epoch-prefix-evidence.md) passes five collection cases and three selected-payment cases.
One treatment retains 130 epochs and 61 accepted mapped baselines, with 59.999 ms of enabled prefix CPU versus 61 ms nominally.
`verify_epoch_prefixes` now replays baseline identity, disabled brackets, enable transitions, source bounds, first samples, and counter subtraction.
It reports `absent`, `unavailable`, or `verified` independently of workload success.
The producer declares `sourceEpochPrefixEvidenceVersion = 1`; that marker requires the prefix field.
Legacy records without either field remain readable with status `absent`.
Earlier unversioned prefix arrays use the same version 1 replay rules.
The report keeps `version` (parser contract) separate from `declaredVersion` (nullable input declaration).
Verified summaries distinguish complete source epochs from the accepted prefix, including the frozen prefix when enabled.
Mapped baseline totals exclude first samples outside that accepted prefix.
Clean collection can retain valid prefixes after a later workload failure. The workload remains failed.
These checks do not promote enabled CPU to selected CPU or qualify prediction accuracy.
Malformed prefixes reject the complete case replay. Removing both fields instead reports legacy absence, never verified evidence.
The parser checks consistency of retained data, not independent authenticity of counter readings.

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
