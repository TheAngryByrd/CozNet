# Terms

- **Region:** A named, synchronous CPU operation on the experiment thread.
- **Virtual speedup:** The requested fractional reduction in the selected region's execution time.
- **Control:** A run that adds proportional delay to every marked region.
- **Treatment:** A run that adds the same proportional delay except in the selected region.
- **Block:** One randomized set containing a control and treatment for each requested speedup.
- **Progress:** One completed unit of identical work. Each measured run must complete the same number.
- **Elapsed reduction:** `1 - treatment elapsed / control elapsed`, computed within each block.
- **Coverage:** Marked region time divided by elapsed time after injected delays are removed.

- **Code binding:** The one optimized native range of each selected and eligible method. It must not change from capture to the end of a run.
- **Tier tag:** The perf-map suffix, such as `[Optimized]` or `[QuickJitted]`, that names the code version of an entry.
- **Bound run:** A target-local run through `TargetLocalProfiler.runBound`. It rejects a bad configuration before the run, and a moved range sets `BindingError`.
- **Run-queue delay:** Time that a runnable thread waits for a CPU, from `/proc/thread-self/schedstat` field 2.
- **Start run-queue delay:** The run-queue delay of a worker from the read before the start barrier to the read after that barrier returns. `StartRunDelayNs` holds it.
- **GC window:** The interval from the `Stopwatch` read before a forced `GC.Collect` call to the read after it, on the monitor thread.
- **Causal profile:** The predicted program gain for each candidate method at each virtual speedup, from `TargetCausalProfile.run`.
- **Target lock:** A non-recursive `TargetLock` whose waits follow the wake rule. A waiter wakes with the token of the release that freed the lock.
- **Target queue:** A bounded `TargetQueue` whose waits follow the wake rule. Each item carries the token of its post.
- **Progress point:** The event that a profile measures. `EndToEnd` is the end of the workload. `Throughput` counts visits during a fixed run time.
- **Causal design:** A parsed `TargetCausalDesign`: the binding, period, eligible symbols, candidates, speedups, blocks, and seed of a causal profile.
- **Profile block:** One baseline experiment and one experiment for each candidate and speedup, run in a seeded random order.
- **Start trace:** Three start points of a worker, from its native start to its work start. Each point holds the wall time, on-CPU time, run-queue delay, faults, and context switches.
- **Start segment:** The interval between two start points. `setup` runs from the native start to the start barrier, and `barrier` covers the start barrier.
- **Residual run-queue delay:** The whole-run run-queue delay minus the start run-queue delay and the wait values. It bounds the run-queue delay during work outside the waits.

Elapsed reduction is not throughput increase. A 20% elapsed reduction implies a 25% throughput increase for fixed work.
