# Owner-drain component cost

The bound workload drains the owner source before selected-probe sample publication and after each selected batch.
Those operations precede idle service reuse.
This diagnostic measures repeated source drains without checksum work, a peer worker, or debt payment.

## Method

Capture: `artifacts/owner-drain-cost/20261016T060212259`.
The retained workload and native profiler binaries come from the idle-service four-arm capture at `f8bf73f`.
The scratch harness source, project, runner, and analyzer are retained with the capture.
The manifest records binary hashes, the resolved container image, and the planned order before execution.
All input hashes remain unchanged.

Six shuffled blocks each contain three fresh processes:

| Stage | Measured operation |
| --- | --- |
| `baseline` | An empty `Action` delegate. |
| `disabled-ring` | The real `LinuxCpuEventSession.Drain` on a disabled, empty perf ring. |
| `owner-active` | The real `BoundSampleCollection.DrainOnOwner` with an enabled task-clock source. |

Each process warms 2,000 calls and measures 500,000 calls.
The source, map, CLR binding, delegates, and collection setup precede the timed loop.
The active stage completes collection after timing and validates its final source evidence through the existing collection path.
The diagnostic runs on Linux x64 with .NET 10.0.0. Tiering and ReadyToRun are disabled.
The shuffle seed is `20261016`. All eighteen planned cases exit zero and pass the diagnostic consistency checks.

## Results

| Stage | Median ns per call | Minimum | Maximum |
| --- | ---: | ---: | ---: |
| Empty callback | 1.312 | 1.282 | 1.638 |
| Disabled empty ring | 10.809 | 10.420 | 17.788 |
| Active owner drain | 78.245 | 75.889 | 83.682 |

The median within-block owner-minus-baseline difference is 38.414 ms over 500,000 calls.
The corresponding ring-minus-baseline difference is 4.749 ms.
All observations remain in the results, including the slowest ring case.

Active cases publish 38–42 samples during their measured loops.
At least 499,958 of 500,000 calls therefore publish no sample in each active case.
Multiple samples can arrive in one drain, so this is a conservative empty-call count.
Active loops allocate 18,536–21,640 calling-thread bytes.
Empty callback and disabled-ring loops allocate zero calling-thread bytes.
The active allocations include sample handling. They do not establish allocation per empty drain.
Final collection retains every measured sample, reports no loss or throttling, and completes all cleanup stages without error.

## Failed harness capture

The earlier capture is `artifacts/owner-drain-cost/20261016T060101303`.
Its six active cases exit 134 because JSON serialization rejects `System.UIntPtr` at `result.Binding.Module`.
Its baseline and disabled-ring cases exit zero.
The failed run remains retained and supplies no active-stage timing result.
The corrected harness serializes sample, batch, counter, and cleanup evidence without the native binding object.
The second capture is a new complete declared block set after that harness fix.

## Limits and next action

The active stage includes CLR lifetime checks, owner checks, snapshot reuse, sample handling, and an enabled event source.
The disabled-ring stage has none of those additional obligations.
Their difference does not isolate the cost of one lifetime check or one source operation.
The loop does not execute the selected checksum workload or contend with a peer.
Its cost cannot establish a full-workload saving, prediction accuracy, or a safe lower drain frequency.

The measurement supports investigating fewer probe callbacks rather than assuming empty-ring reads dominate the remaining mismatch.
The current managed and native parsers accept intervals 1, 64, 256, and 1024 only.
A lower-density experiment needs an explicit interval extension and checks for probe reach, peer service, source collection, and lifetime rejection.
All payment and debt bounds must remain unchanged.

See [observer overhead](../../../docsSrc/Explanations/bound-observer-overhead.md)
and [the preceding four-arm comparison](idle-service-cadence.md#four-arm-comparison-with-idle-reuse).
