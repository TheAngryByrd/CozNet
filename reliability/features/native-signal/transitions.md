# Signal delivery during runtime transitions

The Linux x64 transition experiment passes five native lifecycle cases and eight managed workload cases.
Three shared target-lifetime checks also pass on Windows and Linux.
The [native phase run](phase-timing.md) repeats all fourteen process outcomes with the timed-read backend and passes.
It captures signal interruptions inside JIT compiler code, blocked waits, selected managed work, and the native fixture.
Ineligible interruptions apply no delay. These results do not establish general CLR safety or causal prediction accuracy.

## Protocol

```powershell
dotnet run --project build/build.fsproj -- -t LinuxSignalTransitionsVerify
```

Each managed process registers one target thread and runs one scenario for approximately two seconds.
The controller requests a 50-microsecond delay, reads its acknowledgment, and waits one millisecond before the next request.
Only the previously warmed arithmetic method permits an actual wait.
The runner shuffles eight cases with seed `20261017` and keeps every process outcome.
Every scenario runs with and without an independent allocating thread that requests blocking compacting GC.

The four scenarios are:

- **JIT:** Create fresh dynamic methods, prepare their delegates, and check that each returns `273` for input `17`.
- **Native wait:** Hold the target inside an ordinary P/Invoke until the controller releases its futex wait.
- **Managed wait:** Require an explicit wait-entry marker and `WaitSleepJoin`, then keep the managed event unset until delivery stops.
- **Transitions:** Alternate the selected arithmetic method with a native syscall. Verify the arithmetic checksum and target thread identity.

JIT success requires a saved instruction pointer inside an executable `libclrjit.so` mapping.
Transition success requires saved instruction pointers in both the selected managed range and executable native-fixture code.
The report preserves mapping ranges, image names, permissions, device/inode values, and image hashes.
Unknown addresses retain a separate classification.

The original limits remain unchanged:
5 milliseconds of measured handler work, 100 milliseconds for acknowledgment, and 250 milliseconds per blocking GC call.
An external watchdog limits each process to 30 seconds, followed by forced termination after two seconds.
The measured handler interval excludes entry checks, publication, and return. GC call duration is not direct suspension latency.

## Results

[Retained records](data/run-20261014T002800930-d1a45cf4/summary.json) contain 14 process outcomes, per-request data, GC observations, hashes, and handler disassembly.
One process executes all three deterministic lifetime checks.
The [earlier run](data/run-20261014T001524450-9ed42b70/summary.json) predates the target-lifetime fix and does not establish the failure-path regression result.
The runtime is .NET `10.0.0`. The kernel is `5.15.153.1-microsoft-standard-WSL2`.
The runner used a Windows host and Linux x64 Docker containers with the default syscall policy.

| Case | Requests | Completed operations | Compiler interruptions | Selected interruptions | Native-fixture interruptions |
| --- | ---: | ---: | ---: | ---: | ---: |
| JIT | 881 | 41,451 | 644 | 0 | 0 |
| JIT with GC | 897 | 41,344 | 631 | 0 | 0 |
| Native wait | 908 | 1 | 0 | 0 | 0 |
| Native wait with GC | 909 | 1 | 0 | 0 | 0 |
| Managed wait | 913 | 1 | 0 | 0 | 0 |
| Managed wait with GC | 934 | 1 | 0 | 0 | 0 |
| Transitions | 908 | 602,684 | 0 | 492 | 410 |
| Transitions with GC | 911 | 598,846 | 0 | 489 | 413 |

The native wait uses a libc syscall wrapper. Its interrupted addresses lie outside the fixture image and selected managed range.
The target remains inside the controlled P/Invoke until controller release.
Both blocked-wait scenarios defer every request and complete once after release.
The JIT scenarios also defer every request without delay.

The maximum measured handler interval is `0.310707 ms`.
The maximum acknowledgment is `9.4118 ms`.
The GC cases complete between 95 and 98 forced collections. The maximum GC call is `1.5179 ms`.
Every managed case completes target retirement, thread join, and signal restoration.
This single shuffled block does not establish a statistical GC or throughput effect.

| Requirement | Evidence |
| --- | --- |
| Keep failed targets alive until sending stops | `FailedTargetWaitsForSenderStop` passes and detects removal of the shutdown handshake. |
| Keep completed targets alive until sending stops | `CompletedTargetWaitsForSenderStop` passes. |
| Retain pending ownership | `PendingRequestPreventsRetirement` passes. |
| Preserve occupied signals and reject blocked registration | Native `occupied` and `blocked` cases pass. |
| Preserve request ownership and reject stale sequence reads | Native `lifetime` case passes. |
| Reject retirement while delivery remains pending | Native `pending-shutdown` case fails retirement, then drains delivery and completes normal teardown. |
| Retain ownership after target exit with queued delivery | Native `exit-with-pending` case rejects new requests and restoration. It exits with native state resident. |
| Interrupt compiler execution without waiting there | Both `jit` cases reach executable `libclrjit.so` and return verified dynamic-method results. |
| Defer blocked waits | All four `native-wait` and `managed-wait` cases apply no delay. |
| Exercise managed/native transitions | Both `transitions` cases reach selected and native-fixture instructions. |
| Preserve platform boundaries | The shared harness uses `IBoundedInterventionBackend` and `ISignalTransitionOperations`. Windows rejects the Linux backend before native loading. |

`LinuxSignalVerify` also passes its original nine cases after these changes.
The public serial engine and native handler implementation remain unchanged.

## Failure-path regression

A registered target must not exit while the controller can still send to its native thread ID.
Otherwise, a reused thread ID could direct a late signal to another thread.
Both signal workloads use `SignalTargetLifetime` to hold completed or failed targets until the controller closes delivery.
The target retires only after that handshake and only when requests are drained.

An isolated manual mutation removed `deliveryStopped.Wait();` from this helper.
Both the baseline and mutant built in Release. The baseline passed all three checks on .NET `10.0.12` on Windows.
The mutant failed `FailedTargetWaitsForSenderStop` with `joinedBeforeStop=True` and `retiredBeforeStop=True`.
Neither process timed out. [Mutation records](data/target-lifetime-mutation/summary.json) preserve source fingerprints and raw process verdicts.
The [assertion failure](data/target-lifetime-mutation/failure.txt) records the observed violation.
This proves sensitivity to the missing handshake. It does not prove all target-exit interleavings.

The isolated snapshots copied the workload sources and resolved their existing project references.
Build and execution commands for each snapshot were:

```powershell
dotnet build <snapshot>/CozNet.LinuxPerf.csproj -c Release -p:BuildProjectReferences=false
dotnet <snapshot>/bin/Release/net10.0/CozNet.LinuxPerf.dll --verify-signal-targets
```

Mutation:

```diff
-                deliveryStopped.Wait();
```

## Remaining limits

An abandoned pending request has no recovery API. Target exit does not manufacture an acknowledgment or permit slot reuse.
The `exit-with-pending` case verifies this explicit unsupported cleanup result. It does not prove successful cancellation.
The experiment does not test arbitrary managed frames, stack overflow, signal chaining, or library unloading.
It does not delay JIT or blocked-wait execution.

Next, connect independent CPU events to bounded signal delay accounting and repeat physical-optimization prediction checks.
Keep the two-percentage-point accuracy gate. Do not treat these signal instruction pointers as CPU samples or causal credits.
Windows, macOS, and other architectures still require platform backends.

See [initial signal evidence](README.md), [JIT reach research](../../../docsSrc/Research/signal-jit-reach.md), and [transition contracts](../../../lode/profiling/signal-transitions.md).
