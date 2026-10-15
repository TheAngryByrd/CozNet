# Cooperative child CPU contract checks

The native cooperative transport has opt-in CPU observations for admitted wait attempts.
These checks cover diagnostic storage and ownership. They do not measure intervention accuracy.

## Deterministic checks

`cpuObservationsPreserveServiceAndOwnership` uses a controlled timing adapter and a real owner thread.
It checks disabled capture, unsupported capability, CPU query failure, wait failure, and pre-wait wall-clock failure.
It also advances the wall clock inside CPU queries to trigger the unchanged handler bound.
The failed child keeps both observations and receives no valid payment.
A CPU query failure alone preserves successful wall-time payment.

`cpuJournalRetainsBoundedChildren` fills the four-child journal and retains the journal-capacity failure.
It checks every generation, command, ordinal, and CPU value.
A second generation contains only its own first record.
Active and foreign-thread diagnostic copies fail.
A diagnostic copy never substitutes for the service snapshot required by release.

`cpuSnapshotContract` exercises the separate C ABI records and the production clock adapter.
It checks short buffers, owner/controller roles, record identity, release authorization, and stale generations.
Linux requires observed CPU readings with nondecreasing counters and positive reported resolution.
Windows requires unsupported status and no clock identity.

## Execution

The native service suite contains 13 CTest entries.
The local Windows and Linux runs use these commands:

```powershell
pwsh -NoProfile -File native/VerifyServiceOwner.ps1 -Platform Host -OutputDirectory artifacts/cooperative-child-cpu-host
pwsh -NoProfile -File native/VerifyServiceOwner.ps1 -Platform LinuxContainer -OutputDirectory artifacts/cooperative-child-cpu-linux
```

The local logs are `artifacts/cooperative-child-cpu-host.log` and `artifacts/cooperative-child-cpu-linux.log`.
CTest details are under each output directory in `Testing/Temporary/LastTest.log`.
These ignored artifacts are not distributed with this document.

## Limits

The managed interop checks also cover CPU captures through receipt parsing, cleanup, and generation reuse on Windows and Linux.
Reported resolution and nondecreasing values do not establish calibrated clock accuracy.
CPU query overhead remains unmeasured in the live cooperative workload.
Other platforms have an unsupported-capability hook but have not been executed for this change.

See [the native API reference](../../../docsSrc/Reference/cooperative-interop.md#optional-child-cpu-api)
and [the earlier wait diagnostic](cooperative-wait-cpu.md).


## First managed sampled capture

Local run `artifacts/bound-child-cpu/20261015T202201197-bf45347d` retains the source snapshot and all three cases.
It used `-SelectedPayment -ChildCpu -PauseTiming -SelectedCadence qualified-sample -SelectedBatches 10000`.
All three managed check groups, both Python suites, and the outcome checks passed.
The selected-payment analyzer suite contains 44 tests.
The zero-speedup case completed without payment. Treatment completed 10,000 batches.
The expected-invalidation case preserved its rejection and did not complete.

Treatment retained 49 peer generations with 274 child waits and 24 selected generations with 24 child waits.
The longest peer wait was 68,191 ns. Its longest handler interval was 69,128 ns.
The longest selected wait was 401 ns. Its longest handler interval was 3,933 ns.
The median observed CPU deltas were 50,400 ns for peer waits and 1,150 ns for selected waits.
CPU deltas include observation work outside the measured wait.
No CPU read failed. No CPU delta exceeded its enclosing handler interval under the replay model.

This capture did not reproduce the earlier overrun.
It does not establish steady capacity, calibrated clock accuracy, or physical prediction accuracy.
The retained source snapshot predates stronger assertions in the standalone managed CPU interop check.
The later test-only change does not change the sampled workload or its native library.


## Original workload length

Local run `artifacts/bound-child-cpu-50k/20261015T203140741-db867626` requests 50,000 batches, matching the earlier failed workload length.
It keeps qualified-sample cadence, pause timing, and all service bounds.
Child CPU capture is enabled. The source and binary hashes remain unchanged during the run.
All three cases and their evidence replay pass.

Treatment retains 245 peer generations with 1,469 waits and 108 selected generations with 108 waits.
The longest peer wait is 230,753 ns. The longest peer handler interval is 234,747 ns.
The longest selected wait is 14,607 ns. The longest selected handler interval is 15,971 ns.
No CPU reading fails or reports unsupported capability. Replay finds no CPU delta beyond its enclosing wall interval.

This run also does not reproduce the earlier failure.
The two new captures do not isolate diagnostic overhead or demonstrate steady capacity.
The earlier failure remains retained and unexplained.
