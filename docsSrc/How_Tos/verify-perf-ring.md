# How to verify the Linux perf ring adapter

Use this native Linux x64 procedure to check the perf ring adapter against a live task-clock event.
It requires the .NET 10 SDK, PowerShell 7, Docker, and the local `coznet-native-build:10.0.100` image.

1. Run this command from the repository root:

   ```sh
   dotnet run --project build/build.fsproj -- -t LinuxPerfRingVerify
   ```

2. Open the new directory under `artifacts/perf-ring`.
3. Inspect `summary.json` and `contracts.log`. Check that `passed` and `inputsUnchanged` are true.
4. Inspect `kernel.txt` and `paranoid.txt`. Record the kernel identity with the result.
5. Inspect `normal.json`, `overflow.json`, and `epochs.json`, including failed results.
6. In `normal.json`, check that `wrapped` is true and the one epoch has `lost` 0.
7. In `overflow.json`, check that the epoch has a positive `lost` value and `captureComplete` false.
8. In `epochs.json`, check three epochs with `lost` 0 and `captureComplete` true.
9. In `epochs.json`, check that each epoch increases both `value` and `samples`.
10. In each epoch, check that `disabled`, `drainComplete`, and `readComplete` are true.
11. Check that each epoch `value` is not less than its `lastCumulativeCpu`.
12. Inspect `mapping` and `records`. Each record lists its accepted tail, IP, time, period, and cumulative CPU.

The runner builds the checks without the signal transport and runs `perf-ring` and `perf-sample-ring` first.
The build requires headers that define `PERF_FORMAT_LOST`. Without them, the perf ring targets do not exist and the build step fails.
The runner then runs each workload mode once in a container with no capabilities, no network, and `seccomp=unconfined`.
The default Docker seccomp policy rejects `perf_event_open`.
The runner retains source hashes, binary hashes, the image identity, and the exit codes.
Do not repeat an unchanged case to obtain a pass.

## What each mode checks

Each mode opens one disabled event on the calling thread and maps one metadata page and its data pages.
The workload thread performs all CPU work, drains, and counter reads.

| Mode | Data pages | Work | Drains during production |
| --- | ---: | --- | --- |
| `normal` | 2 | 500 ms of thread CPU | After each 50 ms |
| `overflow` | 1 | 500 ms of thread CPU | None |
| `epochs` | 2 | Three epochs of 100 ms | After each 25 ms |

Each epoch disables the event, drains through the post-disable head, and then reads the 16-byte count and loss.
The `epochs` mode stops before the next enable if an epoch is incomplete or reports loss.
The workload exits with a failure if it cannot write or close its evidence file.
A one-page data area holds 73 samples of 56 bytes. Without drains, the `overflow` mode must lose later samples.
A passing `overflow` result means that the final read detected loss. It does not mean that capture is complete.
An `overflow` run with zero final loss fails. It is inconclusive, not evidence of complete capture.

## Check target-local dispatch

The same run starts `TargetDispatchWorkload` in four modes. Each mode writes `dispatch-<mode>.json`.
Each perf sample overflow requests one real-time signal to the target thread. Each signal starts one bounded dispatch.
The kernel merges overflows that occur before its deferred wakeup work runs.
A dispatch stops after 250,000 ns or its record budget, whichever comes first. Each closure step can read 8 records.
A masked step blocks the signal while the thread works. Its notifications stay queued until the mask ends.

| Mode | Data pages | Records per dispatch | Work | Expected result |
| --- | ---: | ---: | --- | --- |
| `bounded` | 2 | 8 | Three epochs of 100 ms | Three complete epochs, zero loss, and a wrapped ring |
| `backlog` | 4 | 1 | 60 ms, 30 ms masked, 30 ms, 40 ms masked | Budget stops with backlog, and closure takes more than one step |
| `incomplete` | 1 | 1 | 20 ms, 300 ms masked, 20 ms | A terminal epoch with loss evidence, and a refused resume |
| `cancel` | 4 | 8 | 150 ms, cancelled after 50 ms | Cancelled dispatches, a complete closure, and a refused resume |

1. In every `dispatch-<mode>.json`, check that `passed` is true.
2. Check that `stale`, `reentered`, `foreignSignals`, `sigioFallbacks`, and `logDropped` are 0.
   Check that `duplicateCreate` is 16 (`EBUSY`).
3. Check that `maxHandlerNs` is less than 5,000,000.
4. In `retirement`, check that `blocked`, `disabled`, `asyncCleared`, `unmapped`, `closed`, and `maskRestored` are true.
5. Check that each epoch `accepted` value equals the number of `samples` with that `generation`.
6. In `dispatch-backlog.json`, check that `exhaustedWithBacklog` is positive and `closeSteps` is more than 1.
7. In `dispatch-backlog.json`, check that the retirement `pendingDrained` value is positive.
8. In `dispatch-incomplete.json`, check that `complete` is false and `refusedResume` is 71 (`EPROTO`).
9. In `dispatch-incomplete.json`, check that the epoch `lost` value or the retirement `rawLost` value is positive.
10. In `dispatch-cancel.json`, check that `cancelledDispatches` is positive and `refusedResume` is 71.

A `records_exhausted` or `time_exhausted` dispatch with backlog `remaining` did not drain the ring.
A `drained_snapshot` dispatch reached the head that it read. It does not show source closure.
The `incomplete` mode keeps every accepted sample under generation 1. The epoch record marks that generation incomplete.

## Check native credit and payment

The same run starts `TargetCreditWorkload` in `zero` and `half` modes. Each mode writes `credit-<mode>.json`.
A selected thread and a peer thread share one experiment delay. Each thread credits and pays in its own handler.

1. In both files, check that `passed` is true and `replay.valid` is true.
2. Check that `failure` is 0 and `replay.failures` is 0.
3. Check that `replay.childMax` is not more than 50000.
4. Check that each worker has `epochComplete` true, `lost` 0, and `quiescent` true.
5. Check that each worker `maxHandlerNs` is less than 5,000,000.
6. In `credit-zero.json`, check that `global`, `replay.credited`, and `replay.children` are 0.
7. In `credit-half.json`, check that `replay.credited` equals `replay.selectedSamples` times 500000.
8. In `credit-half.json`, check that `global` minus the peer `local` is not more than 2,000,000.

A valid replay shows that the journals match the ledger rules. It does not show that a prediction is correct.

## Limits

The `PerfRingWorkload` drain loop has no record or time budget. `TargetDispatchWorkload` tests budget-limited drains.
Sample counts are not exact functions of elapsed CPU time. Do not compare them with a computed expectation.
The workload thread is both the producer CPU and the consumer. Cross-CPU publication is not tested.
These checks do not test CLR execution or prediction accuracy.

See [the adapter reference](../Reference/native-perf-decoder.md#linux-adapter) and [the dispatch reference](../Reference/target-sample-dispatch.md).
