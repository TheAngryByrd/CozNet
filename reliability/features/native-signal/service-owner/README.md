# Native owner with Linux signal delivery

The first live transport run passes all 12 CTest cases on Linux x64.
Five cases check the portable owner. Seven cases check the Linux transport or its input boundary.
This result does not establish CLR safety, sampled service capacity, or causal prediction accuracy.

## Retained evidence

- [Manifest and source hashes](manifest.json)
- [CTest log](ctest.log)
- [Spin peer journal](lease-spin.tsv)
- [Sleep peer journal](lease-sleep.tsv)
- [Selected journal](selected.tsv)
- [Pending cancellation journal](pending-cancel.tsv)

The run uses Clang 18.1.3 in the retained native compiler image.
The manifest identifies that image and both native binaries.
The command is:

```powershell
dotnet run --project build -t LinuxSignalServiceVerify
```

## Observations

| Case | Budget, ns | Child requests, ns | Measured wait, ns | Result |
| --- | ---: | --- | ---: | --- |
| Peer, spin | 110,000 | 50,000; 50,000; 10,153 | 110,103 | Three separate matching acknowledgments |
| Peer, sleep | 110,000 | 50,000 | 111,301 | Full overshoot retained |
| Selected, spin | 50,000 | 50,000 | 49,907 | One request, with no retry for the short wait |
| Pending cancellation | 110,000 | 50,000 | 0 | One deferred acknowledgment before release |

The spin child waits are 49,832 ns, 50,015 ns, and 10,256 ns.
Their sum is 110,103 ns.
The final request pays the remaining snapshot debt, without adding new credit.
The sleep child asks for 50,000 ns but measures 111,301 ns.
That overshoot pays the entire snapshot and remains in the journal.

The largest measured handler interval in these records is 111,677 ns.
The largest native acknowledgment interval is 119,154 ns.
These observations satisfy the existing 5 ms handler and 100 ms native observation checks.
The 100 ms first managed receipt requirement remains untested because this run has no managed adapter.

## Lifetime checks

`pendingCancellation` blocks the intervention signal after target registration.
The owner retains the outstanding request after cancellation.
Owner release and shutdown fail while delivery remains pending.
Target-side retirement also fails.
After unblocking delivery, one deferred acknowledgment resolves the request.
The host releases the lease, retires and joins the target, and restores the signal disposition.

`liveTargetBlocksRestoration` verifies that a registered target blocks restoration even when no lease is active.
The failed restoration prevents new submissions.
Restoration succeeds after target retirement and join.

`occupiedSignalRemainsUnchanged` verifies that installation leaves an occupied disposition unchanged.
The lease cases also verify that a thread other than the installing owner cannot submit, read, or restore through the native API.
`parsedRangeAndMode` rejects invalid ranges and wait modes before installation.

## Limits

The target executes a small native assembly loop with known instruction boundaries.
The run contains no managed code, GC, CPU sampling, or concurrent selected credit.
It does not measure snapshot-copy overhead or sustained request service.
It does not evaluate the 2 ms sampled debt limit or the two-percentage-point physical-prediction gate.
No timing case was repeated to obtain a pass.

See [the transport contract](../../../../lode/profiling/linux-service-transport.md) and [the native debt plan](../../../../lode/plans/native-debt-service.md).
