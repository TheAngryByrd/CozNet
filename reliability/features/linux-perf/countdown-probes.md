# Linux samples with countdown probes

Source baseline: `f06ad20`, plus the Linux countdown connection in this commit.
All 15 comparison cases pass the existing sample, credit, wait, checksum, and probe-count checks.
Countdown probes reduce measured cost relative to dense probes in this controlled workload.
They still add work inside the selected method. This comparison does not establish prediction accuracy or exact profiler-CPU exclusion.
The subsequent [physical-work comparison](countdown-prediction.md) fails the unchanged accuracy gate.

## Protocol

Run:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxCountdownVerify
```

Three blocks compare manual probes, dense injected probes, and countdown intervals 64, 256, and 1024.
Each block shuffles the five cases with seed `20261014`.
The archive records the actual order. The seed does not reproduce operating-system scheduling.
Each case runs separate zero-speedup and 50-percent processes with Linux nanosleep and a 10-millisecond thread-CPU cadence.
Each process measures approximately 500 milliseconds of work, followed by a sleep phase and final settlement.
The unchanged default-policy check confirms syscall access or records its rejection.

Zero-speedup cost is `experimentMilliseconds / completedBatches`.
Each block's ratio divides the injected cost by that block's manual cost.
The table reports arithmetic means and the full observed ratio range. It does not report a confidence interval.
Failed cases remain in the comparison record and fail the target. No case failed in this run.

## Results

| Probe mode | Mean microseconds per batch | Mean cost ratio | Ratio range |
| --- | ---: | ---: | ---: |
| Manual | 3.279 | 1.000 | Reference |
| Dense | 330.345 | 100.782 | 98.731–103.070 |
| Countdown 64 | 10.103 | 3.082 | 2.936–3.333 |
| Countdown 256 | 5.190 | 1.584 | 1.541–1.649 |
| Countdown 1024 | 4.054 | 1.237 | 1.209–1.266 |

Each batch executes 10,000 additions.
Dense probes execute 10,002 helpers per batch. Countdown intervals 64, 256, and 1024 execute 157, 40, and 10 helpers, respectively.
Both the selected thread and companion worker must match these exact counts.
The 50-percent cases require live credit and worker waits before sampling stops.
The zero-percent cases require zero credit, zero global delay, and zero worker wait.

The three interval-1024 controls resolve 418/499, 444/500, and 441/500 samples to the work method.
They retain 64, 42, and 41 unmapped samples.
Unmapped samples remain unknown. Do not attribute them to a helper without code-range evidence.

## Limits and evidence

These results support interval 1024 as the next measurement candidate for this loop.
They do not establish a general interval or a CPU-time bound between service points.
Longer iterations can delay service. Method-entry probes retain service across repeated short calls.
The collector still uses a fixed managed method map. It does not track later JIT compilations, unload, or address reuse.
Inline countdown instructions remain inside the selected method and can earn causal credit.
Separate helper methods and unmapped addresses earn no selected credit.
The workload does not fully warm all helper paths before map capture.

Linux used .NET 10.0.0, Ubuntu 24.04.3, and kernel `5.15.153.1-microsoft-standard-WSL2`.
The host used PowerShell 7 and Linux x64 Docker containers.
The portable plan and callback checks also pass on Windows. Windows native regression checks pass.
This does not establish Windows CPU-event sampling or support for other architectures.

[comparison.json](data/countdown/comparison.json) contains every outcome, control cost, ratio, and execution order.
[comparison.zip](data/countdown/comparison.zip) retains all raw records and environment identities.
The [native countdown checks](../runtime-probes/countdown.md) establish boundary counts and long-loop service separately.
See [probe-overhead research](../../../docsSrc/Research/probe-overhead-options.md) for the design and remaining attribution work.
