# Cooperative spin wait and thread CPU

Smoke capture: `artifacts/cooperative-wait-cpu/20261015T195604040`.
Measured capture: `artifacts/cooperative-wait-cpu/20261015T195655436`.
The scratch driver calls the repository's compiled `CooperativeSpinTiming.wait`, requesting 50,000 ns.
It does not load the CLR or execute `CooperativeTarget` or `TargetLease`.

## Protocol

Each of four blocks contains one process alone and one process with two competing threads.
Each target pins itself to the first CPU in its available affinity mask, then creates its peers.
The peers inherit that mask and perform arithmetic until the target finishes.
All eight processes used guest CPU 0.
Each process performs 1,000 warmup calls and retains 10,000 measured calls.
All measurement storage is allocated before the loop. File output follows peer join.

`CLOCK_MONOTONIC` brackets each `CLOCK_THREAD_CPUTIME_ID` read.
A CPU read occurs before and after the actual wait.
`getrusage(RUSAGE_THREAD)` records voluntary and involuntary context-switch deltas around the wider observation bracket.
The records retain read-error codes and do not replace failures with successful zero observations.
The diagnostic checks timestamp order, CPU monotonicity, CPU delta against the enclosing wall interval, and switch-count monotonicity.
These consistency checks assume accurate counters. They do not calibrate clock accuracy or resolution.
The scratch driver has a non-Linux branch that reports unsupported capability.
That scratch branch has not been built or exercised on Windows.
The separate production cooperative CPU fallback has Windows contract checks.

The runner records source and executable hashes, image identity, kernel, compiler, process exits, actual order, and unchanged inputs.
The image is the existing `coznet-native-build:10.0.100` image, pinned by identity in the manifest.
The recorded kernel is `5.15.153.1-microsoft-standard-WSL2`, x86_64.
Seed `20261015` happened to select contention first in all four blocks. Time drift is not separated from order.
Raw TSV rows and metadata remain with the copied driver, runner, headers, and analyzer.

## Results

All eight processes passed the diagnostic checks with unchanged inputs.
The independent analyzer rechecked all 80,000 rows and retained each source-record hash.
A successful diagnostic does not mean the wait met service bounds.

| Block | Alone maximum ms | Contended maximum ms | Contended waits above 5 ms | Median CPU delta among long waits, microseconds |
| --- | ---: | ---: | ---: | ---: |
| 0 | 0.447 | 43.558 | 52 | 65.00 |
| 1 | 0.229 | 40.051 | 51 | 55.10 |
| 2 | 0.212 | 40.085 | 51 | 66.80 |
| 3 | 0.555 | 40.079 | 50 | 59.35 |

The contended cases retain 204 waits above 5 ms. Each long observation bracket has an involuntary context switch.
The cases alone retain none.
Long-wait CPU deltas range from 24,200 ns to 10,264,900 ns. The large delta remains in the evidence.
Most calls remain near the requested 50 microseconds. Small medians do not bound the tail.

## Limits and next action

The records demonstrate long native waits without the CLR, with context-switch evidence under deliberate contention.
The CPU and switch intervals include diagnostic work outside the exact wait interval.
They cannot identify the scheduling event that caused any prior managed failure.
The driver does not exercise the lease, its 5 ms rejection, managed cleanup, or causal prediction.
It does not justify treating every long wait as pure descheduling.
The clock observations add work and can change scheduling.
No observed maximum establishes a future bound.

The next integration should use the existing timing seam and retain request generation, command, owner, and child ordinal.
A diagnostic read failure must preserve the service result and explicit failure metadata.
Shared interfaces must represent unsupported CPU clocks without pretending that a zero value was observed.
The payment clock, debt limit, child request limit, handler limit, and receipt deadline remain unchanged.

See [scheduling explanation](../../../docsSrc/Research/native-owner-scheduling.md) and [the retained managed failure](validation-preparation.md).
