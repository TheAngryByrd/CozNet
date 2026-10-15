# CPU sample clock

The experimental Linux x64 CPU sampler uses `PerfAttribute.TaskClock()`.
Both `LinuxCpuEventSession` and the direct perf workload use this configuration.

| Field | Value |
| --- | --- |
| Attribute size | 96 bytes |
| Event | `PERF_TYPE_SOFTWARE`, `PERF_COUNT_SW_TASK_CLOCK` |
| Nominal sample period | 1,000,000 ns of task-clock CPU |
| Sample fields | IP, TID, TIME, PERIOD, READ |
| Initial state | Disabled |
| Exclusions | Kernel and hypervisor samples |
| Timestamp selection | `use_clockid`, flag bit 25 |
| Clock | `CLOCK_MONOTONIC`, ID 1 at byte offset 92 |
| Counter read format | One unsigned 64-bit counter |

Sample records occupy 48 bytes. The cumulative counter follows the nominal period at byte offset 40.
See [CPU counter evidence](cpu-counter-evidence.md) for counter semantics and endpoint readings.

The sampler fails if `perf_event_open` rejects the configuration.
It does not retry with the default clock.
The Linux ABI stays inside the capability workload. The shared library has no Linux clock dependency.

`--verify-cpu-event-clock` checks the attribute bytes and evidence parser without a Linux syscall.
`--verify-timer-layout` includes the same check.
These checks verify ABI configuration. They do not verify live clock alignment.

## Sampled timer evidence

The sampled timer result includes `sampleClock`.
The optional `ICpuEventSession.ClockEvidence` property supplies evidence through the portable `ISampleClockEvidenceSource` interface.
Backends without this capability return no evidence.

| Field | Meaning |
| --- | --- |
| `source` | Declared timestamp domain and units per second |
| `metadata` | Backend, selected clock, ABI size, runtime, kernel, architecture, and managed frequency |
| `start`, `end` | Raw clock readings before and after the frozen measurement window |
| `observedMappingConsistent` | Both readings pass the portable consistency parser |
| `rejection` | Reason the parser rejected the evidence, or null |
| `errorBoundEstablished` | Always false |

Each reading retains managed `Before` and `After` timestamps around native `CLOCK_MONOTONIC`.
It also retains `NativeNanoseconds`, `TimestampFrequency`, `ContextIdentity`, `MonotonicOffsetNanoseconds`, and `Error`.
The Linux adapter reads the calling thread's current time namespace.
It reads offsets from `/proc/self/timens_offsets`.
It requires the caller's current namespace, the caller's child namespace, and the offset source's namespace to match.
It checks all three identities again after the native reading.
Unavailable evidence remains a recorded rejection.
Expected namespace, offset, and native-read failures retain `Error` and the fields captured before the failure.
For a failed syscall, `Before` can exist while `After` remains zero.
An invalid native value or conversion overflow retains both managed timestamps.
The timer runner hashes the clock implementation and checks with its other source inputs.

`ConsistentSampleClockReadings.Parse` returns a value only when all required evidence agrees.
It requires a monotonic source in nanoseconds, known zero offsets, stable contexts, positive matching frequencies, and ordered clock readings.
Native readings must lie inside their managed brackets after unit conversion.
The two brackets must enclose the frozen measurement endpoints.
The result proves consistency of those observations only.
Sample timestamps still use the ring reader's regression checks.

The [observed membership policy](sample-membership.md) compares source timestamps under an explicit fast-clock assumption.
Prediction eligibility and kernel clock error bounds remain unresolved.
Existing captures retain their original clock configuration.

See [sample time and measurement time](../Explanations/sample-clock-alignment.md) for the limits.
See [measurement boundaries](measurement-boundaries.md) for the endpoint contract.
