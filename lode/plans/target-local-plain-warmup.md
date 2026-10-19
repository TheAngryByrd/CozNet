# Target-local plain warm-up

## User decision

The user decided on 2026-10-19:
- Keep the 2 ms debt bound, measured wall-time payment, the release-token rule, and all acceptance criteria.
- Failed runs stay failed. A qualification must not use only the runs that pass.
- WSL2 stays unqualified where its evidence fails. A bare-metal qualification is deferred, and it is not a prerequisite for the other work.
- Capacity failures and prediction errors without capacity failures are separate problems. The cause of the plain handoff gap is unknown, so it is not a host effect by default.

## Finding

In every retained transaction run, the mean queue handoff of the plain original arm is much longer than that of the profile baseline.
In each run, the gap is the median over the five blocks of the mean plain original handoff, minus the median of the mean profile baseline handoff. The median of these gaps is 102.57 microseconds in the four unpinned `transaction` runs and 103.4 in the three `spintransaction` runs.
Pinning the workers to different physical cores does not close the gap, and those pinned blocks are valid with no debt failure.

The two arms do not warm up in the same way:
- A plain arm is a new process. It calls `Work.selected 1` and `Work.peer 1`, and then it times its first and only run of the workload.
- A profile process runs a full warm-up experiment at speedup 0, and it forces a collection before each experiment. So its baseline runs after the queue, the semaphore, the counter, and the timing paths have run.
- The runners set `DOTNET_TieredCompilation=0` and `DOTNET_ReadyToRun=0`, so a method that a plain arm first reaches in its timed work is compiled inside the timed span. Setup code and the two explicit calls are compiled before it.

The retained records already hold a cold profiled run: the warm-up experiment, with its handoff in `queueStats[0]`.
In each of the nine retained transaction and spin transaction runs, the median warm-up handoff is far above the baseline handoff, and it is below the plain handoff by 15 to 146 microseconds.
In `transaction` run `20261019T150326902-04963faa` the medians are 139.8, 154.5, and 49.9 microseconds. In `spintransaction` run `20261019T150209127-dbb260bc` they are 101.1, 117.8, and 8.9.

## Hypotheses

- H1, cold start: the plain arms time the first run of the workload in a new process. Compilation, first allocation, and first page use add time to the early transactions.
  The cold profiled warm-up covers most of the plain-minus-baseline gap in seven of the nine runs, 53% of it in `transaction` run `20261019T121118409-e589636a`, and 37% in pinned `transaction` run `20261019T151433256-9111c620`. The coverage is the warm-up handoff minus the baseline handoff, divided by the plain handoff minus the baseline handoff. So the records are consistent with H1, and part of the gap stays unexplained.
- H2, observer effect: the profiler keeps the CPUs busy, so a blocked thread wakes sooner in the profile arms.
  The warm-up experiment is profiled but stays slow. This is consistent with a cold-start part of the gap, but it does not isolate H2. Cold costs and an observer effect can both add to the gap.
- H3, shared core: the two workers share one physical core in the plain arms. The pinned runs keep the gap, so H3 does not explain it.
- H4, forced collection: the profile forces a collection before each experiment and the plain arms do not. A matched warm-up includes the same collection.

## Decision

Each throughput-family plain arm runs its full arm once, untimed, before its timed run, and then forces the collection that the profile forces. This matches the measurement paths of the two arms.
- The modes are `plain-throughput`, `plain-pipeline`, `plain-lock`, `plain-spin-lock`, `plain-semaphore`, `plain-transaction`, and `plain-spin-transaction`.
- The warm-up run uses the same code, rounds, threads, and duration as the timed run. It writes its record to `<output>.warmup` next to the timed record, so the outcome stays retained.
- After the warm-up, the arm calls `GC.Collect(2, GCCollectionMode.Forced, true)` and starts the timed run. The profile collects after it builds the workers of an experiment, and the plain arm collects before it builds them.
- The warm-up and the collection come together, so the test cannot separate H1 from H4. The profile baseline also runs after other experiments of its process, so a remaining gap can have other causes.
- The analyzer requires each plain arm of these workloads to have a warm-up record that passed, as a profile requires a passed warm-up experiment. A block without one is invalid.
- The modes `plain` and `plain-phases` do not change. Their timed work calls only `Work.selected` and `Work.peer`, which they already warm, and they serve other suites.
- The profile path, the rule, the bounds, and the criteria of the earlier plans do not change.

## Acceptance criteria

These criteria are fixed before the first run. They add to criteria 1 to 104 and change none.

105. Each plain arm of a changed workload has a warm-up record from the same process that passed. A block without one is invalid, and an analyzer check covers a missing and a failed warm-up record.
106. The test of H1: in one `transaction` run and one `spintransaction` run on the reviewed tree, the gap is at most 51.3 microseconds and at most 51.7 microseconds.
     The gap of a run is the median over its five blocks of the mean plain original handoff, minus the median of the mean profile baseline handoff.
     These are half of the prior medians. If either gap is larger, the matched warm-up did not halve the gap, and this line of work stops. That result alone does not show that H1 is false.
107. The changed suites run once each on the reviewed tree: `throughput`, `pipeline`, `lock`, `spinlock`, `semaphore`, `transaction`, and `spintransaction`. Their criteria do not change, and each failure is retained.
108. After the commit, every source hash in each new manifest equals the SHA-256 of the committed blob. No run is repeated to replace a failure.

See [target-local handoff parts](target-local-handoff-parts.md), [target-local worker pinning](target-local-worker-pinning.md), and [target-local status](target-local-status.md).
