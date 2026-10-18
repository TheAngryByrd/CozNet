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

## Limits

The workload drain loop has no record or time budget. Budget-limited drains are not tested live.
Sample counts are not exact functions of elapsed CPU time. Do not compare them with a computed expectation.
The workload thread is both the producer CPU and the consumer. Cross-CPU publication is not tested.
These checks do not test target-local notification, native credit, payment, signal handlers, or CLR execution.

See [the adapter reference](../Reference/native-perf-decoder.md#linux-adapter).
