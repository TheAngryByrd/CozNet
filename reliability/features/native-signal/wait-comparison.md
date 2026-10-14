# Sleep and spin comparison

Spin passes all three sampled treatment cases in this run. Sleep fails all three.
Spin also consumes more process CPU time in the completed treatments.
These results support further spin experiments. They do not establish stable capacity, general CLR safety, or prediction accuracy.
Sleep remains the default backend mode.

## Contract

`SignalWaitMode.Parse` accepts `sleep` and `spin` before native installation.
The native mode is immutable after installation. Invalid modes do not consume installation ownership.
The original `coz_signal_install` export still selects sleep.
The result and phase-timing layouts remain unchanged.

Both modes use the same monotonic deadline and allow at most 50 microseconds of requested delay.
Spin uses direct clock syscalls and stops after at most 4,096 iterations.
Clock failure, backward clock readings, and iteration exhaustion produce native errors.
The iteration cap cannot bound wall-clock time when the operating system deschedules a thread.
The measured handler limit remains 5 ms. Acknowledgment remains limited to 100 ms.

The first build failed the existing no-call disassembly check because the compiler emitted a helper call.
The final helper is forced inline. The final handler passes that unchanged check.
The archive retains the rejected disassembly and source separately from the measured implementation.

## Experiment

Command: `dotnet run --project build/build.fsproj -- -t LinuxSignalWaitCompare`.
The [archive](data/wait-comparison/run-20261014T024226852-ac13efd3.zip) contains both suites and every outcome.
The overall command fails because its sleep treatments fail.
The parent runner continues into the sampled suite if the basic suite fails.

The basic suite runs twelve cases in seeded order: both modes, baseline/zero/delay service, and forced GC enabled/disabled.
Seed `20261016` controls that order.
The sampled suite runs three blocks. Each block contains both modes at zero and 50 percent.
Seed `20261018` shuffles each block independently.
Each sampled case has a 250 ms warmup and a two-second measurement window.
These seeds reproduce case order, not scheduling.

This workload fixes two arithmetic workers, one selected sampling source, and one controller.
It does not explore different worker counts, CPU quotas, sample periods, GC workloads, or applications.
The sampled suite does not force GC. The separate basic suite does.

## Results

All twelve basic cases pass, including both positive-delay modes with forced GC.
The parser, phase-timing, occupied-signal, blocked-registration, lifetime, and wait-mode checks pass.
The four sampled controller checks also pass.
All six sampled zero-percent cases pass.

| Sampled treatment | Protocol | Capacity | Peak peer debt (ms) | Maximum handler interval (ms) | Process CPU (ms) |
| --- | --- | --- | ---: | ---: | ---: |
| Block 1, sleep | Pass | Fail | 2.437265 | 4.529977 | 5035.203 |
| Block 1, spin | Pass | Pass | 0.500199 | 0.299032 | 5984.073 |
| Block 2, sleep | Pass | Fail | 4.726682 | 1.039044 | 5026.228 |
| Block 2, spin | Pass | Pass | 1.549478 | 0.219950 | 5973.184 |
| Block 3, sleep | Fail | Fail | 3.880264 | 6.976811 | Incomplete |
| Block 3, spin | Pass | Pass | 1.899863 | 3.526523 | 5918.279 |

The debt bound remains 2 ms. Handler maxima include warmup and settlement requests.
The third sleep treatment fails during measurement at 1865.68 ms.
Its partial CPU total is retained in raw evidence and is not a completed two-second result.
The third spin treatment passes near the debt limit. Three passes do not prove stable capacity.

`Environment.CpuUsage.TotalTime` measures CPU across all process threads, including kernel work.
The recorded unit is a 100 ns `TimeSpan` tick, not a guaranteed measurement resolution.
CPU time can exceed wall time because threads execute on separate cores.
The measurement includes controller, worker, and GC activity. It does not isolate CPU consumed inside the spin loop.
Counter reads add diagnostic overhead to both modes.
Basic forced-GC call durations remain below the unchanged 250 ms bound.
They do not directly measure suspension latency.

## Evidence and next step

The archive retains 32 measured source/binary files with verified hashes.
`AnalyzeTiming.ps1` verifies 25 sampled hashes and reconciles 22 phases from eleven completed processes.
It marks the failed sleep process incomplete.
`AnalyzeCounters.ps1` checks 24 counter windows, including the partial failure window and process CPU monotonicity.
`AnalyzeWaits.py` verifies mode coverage, outcomes, teardown, positive waits on both workers, and reported metrics.
No failed measurement was repeated to replace its result.

Next, test forced GC in the sampled spin workload and exercise spin during runtime transitions.
Then compare sampled predictions with physical reductions in work under matched controls.
The two-percentage-point prediction gate remains unchanged.
