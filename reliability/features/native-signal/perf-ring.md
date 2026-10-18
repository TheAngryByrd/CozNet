# Linux perf ring capture

Evidence: `artifacts/perf-ring/20261018T053808011-d4d07dfe`.

`LinuxPerfRingVerify` builds the perf ring checks and runs each live workload mode once.
The kernel is `5.15.153.1-microsoft-standard-WSL2` on x64, with `perf_event_paranoid = 2`.
The container drops all capabilities, has no network, and uses `seccomp=unconfined`.
The `perf-ring` and `perf-sample-ring` contract checks pass. All source and binary hashes remain unchanged.
Every mode uses the same 96-byte attributes with `PERF_FORMAT_LOST` and a 1,000,000 ns period.
Each mode file retains the mapping metadata and every decoded record with its accepted tail.

| Mode | Data bytes | Samples | Accepted tail | Wrapped | Final value (ns) | Final lost | Capture complete |
| --- | ---: | ---: | ---: | --- | ---: | ---: | --- |
| `normal` | 8192 | 471 | 26376 | Yes | 500117700 | 0 | Yes |
| `overflow` | 4096 | 73 | 4088 | No | 500022700 | 390 | No |
| `epochs` | 8192 | 278 | 15568 | Yes | 300123100 | 0 | Yes |

Every retained record advances the accepted tail by 56 bytes and has the fixed period.
Record timestamps and cumulative CPU readings do not decrease. Every epoch completes disable, final drain, and a 16-byte read.
In `normal` and `epochs`, the workload drains between CPU slices and wraps the data area.

The producer is the task-clock timer on the CPU that runs the workload thread.
Kernel publication therefore interleaves with drains on one thread. This capture does not observe cross-CPU head and tail visibility.

The `epochs` mode closes three epochs at cumulative values 100055100, 200083300, and 300123100 ns.
Their cumulative sample totals are 88, 182, and 278. Each epoch increases both the value and the sample total.
Loss stays zero, and each value is not less than the last sample reading.

The `overflow` mode never drains during production. Its one-page data area accepts 73 samples.
The ring contains no loss record. The final read reports 390 lost samples, and `captureComplete` is false.
A count-only source would show 73 valid samples and no loss evidence.
The 463-event total is consistent with the 471 samples in `normal` for the same work.
The two modes use separate events, so this comparison is not a reconciliation.

## Limits

The workload drain loop has no record or time budget. Bounded dispatch remains untested live.
These runs do not test target-local notification, signal handlers, native credit, payment, or CLR execution.
They do not establish service capacity or prediction accuracy.
`PERF_FORMAT_LOST` on this kernel is a Microsoft backport. Other kernels and seccomp policies are not verified.

See [the adapter reference](../../../docsSrc/Reference/native-perf-decoder.md#linux-adapter), [the procedure](../../../docsSrc/How_Tos/verify-perf-ring.md), and [the plan](../../../lode/plans/target-local-samples.md).
