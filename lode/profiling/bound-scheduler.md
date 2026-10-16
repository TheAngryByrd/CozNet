# Bound scheduler capture

The bound workload uses `IBoundSchedulerBackend` for platform session creation and clock readings.
`SchedulerCaptureGroup` is shared with sampled timers and preserves their retirement contract.
Roles are controller, selected, and peer at indices 0, 1, and 2.
Factories execute on the corresponding owner threads.

```csharp
scheduling.Register(2);
```

`BoundSchedulerCapture` stores a clock bracket before work and another before collector shutdown.
The native nanosecond readings share the backend's scheduler clock domain.
They preserve epoch uncertainty; rate equality and physical accuracy remain separate assumptions.
Clock errors remain diagnostic failures while collectors still stop.
Failed disposal retains target ownership and prevents worker joins.
Repeated completion preserves the original result.

```mermaid
flowchart LR
    Owners[Register three owners] --> Begin[Clock bracket]
    Begin --> Work[Sampled work and progress snapshots]
    Work --> End[Clock bracket]
    End --> Stop[Stop all collectors]
    Stop --> Retire[Permit retirement only after disposal]
```

Linux supplies existing perf switch records and CLOCK_MONOTONIC.
Activation requires selected payment and peer progress so replay can bind the peer OS identity.
Other platforms can supply the same interface.
The runner binds `-Scheduler` activation to the manifest and outcome records.
Offline replay checks owner identities, loss, clock brackets, and sample collection bounds.
Correlation reports conservative overlap bounds for closed observed intervals under an equal-rate assumption.
Each marker retains its query timestamps separately from the analyzed window.
Missing query windows remain unavailable. Failed workload outcomes remain failed.
Python fixtures cover these boundaries. Controlled Linux runs retain loss-free evidence in both CPU configurations.
The ordinary run passes all three cases. The one-CPU treatment fails at 2,000,132 ns of peer debt.
Its pending request and later query lie inside one observed peer switch-out interval under the clock bounds.
See [the retained evidence](../../reliability/features/native-signal/bound-scheduler.md).
This result does not authorize debt forgiveness or establish stable capacity.
The evidence record states the accepted diagnostic and test-coverage limits.

See [the reference](../../docsSrc/Reference/bound-scheduler-capture.md),
[scheduler records](scheduler-records.md), [progress markers](cooperative-progress-marker.md), and [the index](../lode-map.md).
