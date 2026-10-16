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

## Bound sampled evidence

`COZNET_BOUND_SAMPLE_CLOCK=1` enables optional clock capture in the finite bound sampled workload.
Absent values and `0` disable capture. Other values fail configuration parsing.
The runner exposes `-BoundSampleClock` and requires `-FiniteMeasurement`.
The source supplies `ISampleClockEvidenceSource` through the same portable `ClockEvidence` hook.

The first reading occurs after source creation and before source enable.
The second reading occurs after measurement freeze and before source cleanup.
On workload failure, cleanup attempts the second reading if capture remains active.
Repeated completion does not repeat a clock read.
The final read can add source-tail observations after the frozen endpoint.

| Field | Meaning |
| --- | --- |
| `boundSampleClockEnabled` | Boolean activation |
| `boundSampleClockEvidenceVersion` | Format version, currently `1` |
| `boundSampleClock` | Null when disabled, otherwise the evidence record |
| `Status` | `not-started`, `started`, `complete`, `failed`, or `unavailable` |
| `Source`, `Metadata` | Declared clock domain and backend metadata |
| `Start`, `End` | Retained raw readings, or null before each successful read |
| `Failure` | First backend exception, or null |
| `ObservedMappingConsistent` | Complete readings pass the existing consistency parser |
| `Rejection` | Backend failure or consistency rejection, or null |
| `ErrorBoundEstablished` | Always false |

`unavailable` means that the source supplied no clock provider.
`failed` retains a backend exception without retrying.
`complete` means that both reads returned. Their contents can still fail consistency parsing.
Diagnostic rejection does not change workload success, sample credit, measured payment, or timing bounds.

Both bound replay analyzers recompute consistency from the raw evidence.
They accept legacy records with none of the three declaration fields.
They reject partial declarations, malformed values, and consistency claims that disagree with the raw evidence.
Their `boundSampleClock` result reports `disabled`, `rejected`, or `consistent`.
Legacy records produce a null result.
Consistent observations do not establish sample membership, exact CPU attribution, or prediction accuracy.
An empty backend error remains a diagnostic rejection with an empty reason.

See [the capture procedure](../How_Tos/run-bound-selected-payment.md#capture-sample-clock-evidence) for runner use.
See [sample time and measurement time](../Explanations/sample-clock-alignment.md) for the limits.
See [measurement boundaries](measurement-boundaries.md) for the endpoint contract.
