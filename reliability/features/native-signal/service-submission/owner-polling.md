# Native owner polling comparison

The bounded policy does not remove the capacity failure in this controlled run.
Both 50-percent cases fail during warmup. The default remains `yield`.

## Evidence

Run: `artifacts/linux-sampled-service/20261014T180225129-83f9233c`.
Command: `dotnet run --project build/build.fsproj -- -t LinuxServiceOwnerPolling`.
The manifest records source and binary hashes. The final hash check reports unchanged inputs.
The order is yield at zero and 50 percent, then poll-64 at zero and 50 percent.
Each case runs in a separate process. This single fixed-order comparison cannot establish a performance ranking.
Both initialization stages, submission timing, and four scheduler streams are enabled.
All existing limits remain unchanged.

| Policy | Speedup | Result | Failure boundary | Debt at failure boundary |
| --- | ---: | --- | --- | ---: |
| yield | 0% | Pass | None | 0 ns |
| yield | 50% | Fail | Warmup, 23.8331 ms | 2,999,937 ns |
| poll-64 | 0% | Pass | None | 0 ns |
| poll-64 | 50% | Fail | Warmup, 28.4875 ms | 4,999,950 ns |

The separate peak-debt records report 2,999,964 ns and 4,780,075 ns for the two treatments.
The failure boundary and peak-debt records represent different observations.
All four scheduler analyses pass integrity checks. All targets join and all sessions restore.
Seven Linux native checks pass, including both ABI policies.
Managed accounting, scheduler, shutdown, and initialization checks also pass.
Five portable owner checks and managed accounting checks pass on Windows.
These checks do not make the failed workloads pass.

## Scheduling observation

In poll-64, peer command 18 starts and completes in warmup.
Child 5 has 10,543,082 ns between its last handler clock reading and owner observation.
Owner switch-out overlaps 10,539,447 ns of that interval.
Controller switch-out overlaps 8,227 ns. Target switch-out overlap is zero.
The owner switch-out record has `preempt: false`.
The record does not identify the operation that caused the switch-out.
These clock readings do not mark exact handler entry or return.

## CPU observations

Process CPU differences use `ProcessCpuTicks` at the stage start and retained stage boundary.
One tick is 100 ns. These are process totals, not owner-only CPU measurements.

| Policy | Speedup | Stage | Process CPU difference |
| --- | ---: | --- | ---: |
| yield | 0% | Two-second measurement | 6,036.135 ms |
| poll-64 | 0% | Two-second measurement | 6,030.544 ms |
| yield | 50% | Failed warmup | 69.687 ms |
| poll-64 | 50% | Failed warmup | 85.475 ms |

Zero-speedup cases do not exercise active debt payment.
The treatment durations differ and neither reaches measurement.
These totals cannot establish the steady-state CPU cost of active polling.

## Consequence

Finite polling preserves the protocol but retains the owner's scheduling dependency between children.
Do not promote poll-64 as a fix or change the debt bound.
A target-local lease prototype needs its own eligibility, cancellation, journal, receipt, and lifetime contracts.
Physical prediction accuracy and general CLR safety remain unverified.

See [owner scheduling](owner-scheduler.md), [the implementation explanation](../../../../docsSrc/Research/native-owner-scheduling.md),
and [the polling reference](../../../../docsSrc/Reference/native-service-accounting.md#portable-owner-polling-policy).
