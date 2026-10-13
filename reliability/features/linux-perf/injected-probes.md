# Linux samples and automatic probes

Source baseline: `aa33f10`, plus the controlled injected-probe connection in this change.
The connection passes its runtime checks, but its overhead is too high for useful profiling of this workload.
The zero-speedup injected case costs 101.5 times as much per batch as the manual case.
This is one diagnostic comparison, not a statistical estimate or a prediction-accuracy result.

```sh
dotnet run --project build/build.fsproj -- -t LinuxInjectedVerify
```

The target builds the Linux native profiler and the managed workload.
It runs manual probes first, then injected probes, with Linux nanosleep and a 10 ms thread-CPU cadence.
Each mode runs access-policy, 50-percent, and zero-percent cases in bounded containers.
The runner retains each invocation under a unique `artifacts/linux-injected/` directory.
`last-run.txt` identifies that directory.

`COZNET_PROBE_TARGET=linux-perf` selects `CpuWorkload.Run` for native rewriting.
`COZNET_PROBE_MODE=injected` binds each managed thread to its own callback.
This mode omits the manual probe after each work batch.
Each batch must execute exactly 10,002 injected calls: one entry and 10,001 backward-branch checks.
The selected thread applies CPU sample credits. The companion thread pays delay through its injected callback.
The selected thread must apply samples and produce worker waits before sampling stops.

| Mode | Speedup | Batches | Selected-thread probes | Worker probes | Samples | Work-method samples | Live credit | Live worker wait |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Manual | 0% | 154,222 | 0 | 0 | 500 | 483 | 0 ms | 0 ms |
| Manual | 50% | 148,728 | 0 | 0 | 499 | 477 | 233.5 ms | 228.26 ms |
| Injected | 0% | 1,520 | 15,203,040 | 15,873,174 | 500 | 4 | 0 ms | 0 ms |
| Injected | 50% | 1,496 | 14,962,992 | 15,483,096 | 495 | 3 | 1.5 ms | 5.68 ms |

Every case preserves the arithmetic checksum and passes loss, throttling, mapping, and settlement checks.
The control cost is 0.0032455 ms per batch with manual probes and 0.3294311 ms with injected probes.
Most samples in the injected runs fall outside the work method.
Every backedge calls a managed helper, updates a thread-local counter, and invokes a callback that checks cadence.
The CPU clock gate does not remove the per-probe elapsed-timestamp read.
The next optimization must reduce that work while retaining bounded loop coverage.
Do not interpret the three selected treatment samples as accurate coverage of an uninstrumented program.

The missing-rewrite check selects the other native target while requesting injected probes.
It fails during warmup with status 1, zero rewritten methods, and zero injected calls.
The callback checks reject nested bindings and disposal from another thread.
They also verify thread isolation, detachment, and rebinding on Windows and Linux.
The Windows `NativeVerify` regression passed after the shared native target-selection change.

Raw records and the comparison summary are in [data/injected](data/injected/).
The archive contains both modes, including access-policy outcomes and environment records.
The sanitized negative result retains the process exit code and assertion message.
The native library and record ABI remain Linux x64 for this path.
General method selection, source lines, JIT lifecycle, synchronization, and async behavior remain unsupported.

See [Linux contracts](../../../lode/profiling/linux-perf.md) and [prediction failures](prediction.md).
