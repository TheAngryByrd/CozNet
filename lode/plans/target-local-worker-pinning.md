# Target-local worker pinning

## Finding

The remaining qualification failures follow host scheduling on the WSL2 host with 4 CPUs:
- A thread that can run can settle its debt late, after a late start or a wake.
- In lock run `20261019T131912794-bfae28ce`, one child payment of 50 microseconds took 3.35 ms, and the other worker then owed more than 2 ms.
- In the transaction suites, the plain arms spend about 100 microseconds more in each queue handoff than the profiled arms. The records do not show why.
Hypothesis, not shown: the two workers share CPU time with each other or with other threads, for example on one physical core.

## Topology

The runtime container shows 2 physical cores with 2 hardware threads each. CPUs 0 and 1 share core 0, and CPUs 2 and 3 share core 1.
A pin inside the container cannot stop the Windows host from pausing a virtual CPU. Only the two workers are pinned, so runtime threads can still share their CPUs.

## Decision

The sample can pin each worker thread to one CPU. This is a diagnostic of the sample, and the library does not change. No rule, bound, or criterion changes.
- The environment variable `COZNET_WORKER_CPUS` holds one CPU number for each worker, for example `0,2`. Worker i calls `sched_setaffinity` for its own thread with CPU i at the start of its work, inside the timed span in both arms. So each worker makes one timed affinity call. It moves the thread only when the thread runs on another CPU, so the cost can differ between arms.
  The pin starts after native sampling starts, so it cannot change a failure before the pin.
  The sample accepts two CPUs from 0 to 63, and the runner accepts only `'0,2'` and only the lock and transaction workloads.
- Each worker reads `sched_getcpu` at the end of its work. Each record holds `workerCpus`, the requested CPUs, and `workerCpusSeen`, the CPUs that the workers read, in a plain record and for each experiment of a profile record.
- A failed `sched_setaffinity` stops the process with exit code 3, so the block is invalid, and the records of that process are lost.
- `VerifyTargetLocalProfile.ps1 -WorkerCpus '0,2'` passes the variable to each container and writes its runs to `artifacts/target-local-<workload>-pinned`. The analyzer and its criteria are those of the workload. The manifest holds the CPUs.
The pinned runs are diagnostics. They have no evidence page, and they do not count toward the run history of their workload.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 101 and change none.

102. In each pinned run, each record holds `workerCpus` equal to `[0, 2]`, and each value of `workerCpusSeen` equals the requested CPU of its worker. A block that fails this is invalid.
103. The pinned `transaction`, `spintransaction`, and `lock` suites each run once on the reviewed tree, with the unpinned suites of the same tree. The status reports, for each pinned run, the point results, the debt failures, and for the transaction suites the baseline values of criteria 96 and 100, next to the unpinned run of the same tree. It states a cause only if the records show it.
104. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. Each suite runs once on the reviewed tree, and no run is repeated to replace a failure.

See [target-local handoff parts](target-local-handoff-parts.md), [target-local wait delay](target-local-wait-delay.md), and [target-local status](target-local-status.md).
