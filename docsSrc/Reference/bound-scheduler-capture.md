# Bound scheduler capture

The bound selected-payment workload accepts an optional `IBoundSchedulerBackend`.
The backend supplies per-thread scheduler sessions and a nanosecond clock in the same clock domain.
Platform calls remain outside shared accounting and capture ownership.

## Capture roles

| Index | Owner |
| --- | --- |
| `0` | Native API controller thread. |
| `1` | Selected application thread. |
| `2` | Peer application thread. |

Each owner creates its own session before sampled work starts.
`SchedulerCaptureGroup` also supplies the existing timer workload retirement gate.
Completion stops and disposes every registered collector, including after partial registration.
Failed disposal prevents target retirement and worker joins.
Capture errors and loss records remain errors after successful disposal.

## Clock observations

`BoundSchedulerCapture.Begin()` records a clock observation after registration.
`Finish()` records another observation before stopping collection.
Each observation contains managed `Before` and `After` timestamps, native `Nanoseconds`, and managed `Frequency`.
`Failure` retains a query error or invalid timestamp.
The brackets do not assume equal clock epochs.
They do not independently establish equal clock rates or physical clock accuracy.

Completion retains the clock domain, stopped state, both observations, three captures, and failures.
Repeated completion returns the retained result without querying clocks or stopping collectors again.
Scheduler failures do not replace a previously captured workload failure.
They prevent the overall diagnostic run from passing.

## Linux backend

`LinuxBoundSchedulerBackend` uses the existing Linux x64 scheduler sessions and `CLOCK_MONOTONIC`.
`COZNET_BOUND_SCHEDULER=1` selects the backend in bound workload routes.
The workload requires selected payment and `COZNET_PEER_PROGRESS=1` when this backend is enabled.
Unspecified or `0` disables it. Other values reject activation.

`native/VerifyBoundSampledPayment.ps1 -Scheduler` requires `-SelectedPayment -PeerProgress`.
The runner records activation in the manifest and checks each workload outcome.

## Offline correlation

`native/AnalyzeBoundScheduler.py` replays selected-payment evidence before it computes scheduler overlap.
It requires three distinct owner identities in one process, stopped collectors, and loss-free records.
The selected identity must match source samples. The peer identity must match progress observations.
Source sample timestamps must fall within assured selected scheduler collection.

Clock brackets bound the epoch offset under an explicit equal-rate assumption.
Inconsistent brackets reject correlation. Query windows must fall within the clock anchors and assured scheduler collection.
The output reports lower and upper overlap bounds for closed observed switch-out intervals only.
Intervals use half-open boundaries. A nonempty window can end at the switch-in timestamp.
An empty window does not establish containment in a switch-out interval.

Each window includes `markerQueryStarted` and `markerQueryFinished` in managed timestamp units.
The marker describes that query. It does not establish the marker phase throughout an earlier request-to-debt window.
Records without peer queries report `state: unavailable` and `reason: no-peer-query-windows`.
Valid evidence can retain a failed workload through `casePassed: false` and `completed: false`.

Censored intervals do not contribute to overlap bounds.
Zero observed overlap does not prove continuous execution.
Controlled Linux captures verify this path on ordinary and one-CPU configurations.
The one-CPU treatment retains a debt failure with verified scheduler evidence.
Windows has managed capture checks but no live scheduler backend in this change.
This capture does not establish stable capacity, low overhead, or scheduling cause.

See [the capture procedure](../How_Tos/capture-bound-scheduler.md),
[cooperative progress markers](cooperative-progress-marker.md), and [sampled timers](sampled-timer.md).
