# Bound CPU session

`BoundCpuSession` parses counter evidence across repeated source enable and disable operations.
It is an internal component under development.
`BoundSampleCollection.StartPausable` uses this parser for repeated source transitions.
Its default `Start` path retains the one-shot `BoundCpuEpoch` contract.

## Initial state

`ParseFresh(snapshot)` requires a fresh, disabled source with epoch zero and a zero CPU counter.
The transition journal must be empty.
The snapshot must contain valid counter evidence without read failures, overflow, or unrecorded cleanup.

`Initial` retains this evidence. `Current` identifies the latest accepted source state.
Each accepted transition returns a new session.
Built-in immutable journal histories remain shared. Other journal lists are copied into read-only collections.
Earlier session values retain their original state.

## Transitions

| Operation | Required state | Accepted evidence |
| --- | --- | --- |
| `Enable(snapshot)` | Disabled | One successful enable transition and the next source epoch |
| `Disable(snapshot, cpuNanoseconds)` | Enabled | One successful disable transition in the current source epoch |
| `Close(snapshot, cpuNanoseconds)` | Disabled after at least one epoch | The same journal, source epoch, and final CPU counter |

Each transition must preserve source identity, timestamp frequency, and the complete previous journal prefix.
A direct child of the accepted immutable history proves prefix preservation without enumeration.
Other histories require a full value comparison of the previous prefix.
Equivalent histories remain valid even when they do not share nodes.
Ancestry does not bypass validation of the new transition, counter, or clock.
The cumulative CPU counter cannot decrease.
Disable requires the supplied CPU value to equal the snapshot counter.
The transition must follow the previous reading, and the new reading must follow the transition.

Failed operations, changed journal entries, extra transitions, and invalid counter evidence cause `InvalidDataException`.
Closing requires an unchanged counter after disable.
A later closing reading must not overlap the previous reading.

## Limits

This parser checks evidence. It does not enable, disable, drain, or dispose a source.
Source epochs do not establish thread eligibility epochs.
The parser does not detect sample loss or establish producer-stop semantics.
Collection must retain cumulative loss evidence across all source epochs.
The latest immutable entry is directly accessible. Older index access walks the history.
Enumeration creates an ordered array, so serialization and fallback validation still have history-dependent costs.
The [component and live measurements](https://github.com/TheAngryByrd/CozNet/blob/master/reliability/features/native-signal/counter-history.md) retain timing variation and a failed continuous treatment.
They do not establish stable profiler capacity or prediction accuracy.

See [the collector contract](pausable-sample-collector.md) and [perf pause ownership](../Research/perf-pause-ownership.md).
