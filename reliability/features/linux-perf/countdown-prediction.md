# Countdown prediction against physical work

The interval-1024 prediction run fails the unchanged two-percentage-point accuracy gate.
Lower probe cost does not establish accurate causal predictions.

## Protocol

Source baseline: `f06ad20`, plus the Linux countdown connection and prediction-runner options in this commit.
Run:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxCountdownPredictionVerify
```

The target uses the existing eight-block protocol with seed `20261013` and two-second measurement windows.
Profile cases use injected probes, interval 1024, Linux nanosleep, and a 10-millisecond thread-CPU cadence.
Physical cases run without the profiler or CPU sampler.
The physical optimization replaces half the additions with an equivalent constant sum.
All cases preserve the same checksum. Every profile case passes its count, sample, live-credit, and wait assertions.

The [existing prediction protocol](prediction.md#protocol) defines effective duration, paired error, and the Student-t interval.
No threshold, block, or failed result was removed or changed.

## Result

| Measure | Result |
| --- | ---: |
| Mean predicted reduction | 44.99% |
| Mean physical reduction | 49.87% |
| Mean prediction error | −4.87 percentage points |
| 95% interval half-width | 2.07 percentage points |
| Absolute mean error plus half-width | 6.95 percentage points |
| Required maximum | 2.00 percentage points |
| Mean zero-speedup overhead against physical baseline | 26.36% |

The target writes `summary.json`, then fails with the accuracy error.
This is a measured limitation of the current sampled prototype, not a passing accuracy test.
The eight-block interval assumes independent block errors and does not cover systematic bias.

## Remaining work

Inline gates remain in the selected method's samples. Helper methods and unmapped samples earn no selected credit.
The fixed map also misses code compiled after capture.
These are attribution limits, but this experiment does not isolate their effects from overhead or scheduling.
Removing inline gate samples alone does not prove that the prediction bias will improve.
Profiler work also affects elapsed duration, register allocation, and the proportion of samples that match the target.
Do not subtract an average overhead percentage and claim that individual observations are corrected.

The [attribution investigation](../../../docsSrc/Research/injected-cpu-attribution.md) proposes a separate diagnostic for native-range ownership.
The [cost comparison](countdown-probes.md) measures the cheaper probe path independently.

## Raw evidence

The run used .NET 10.0.0 in Linux x64 containers on kernel `5.15.153.1-microsoft-standard-WSL2`.
[run-22ccda12.zip](data/countdown-prediction/run-22ccda12.zip) contains all 32 case records, execution order, environment, and summary.
[run-22ccda12-summary.json](data/countdown-prediction/run-22ccda12-summary.json) permits direct inspection of the failed gate.
The earlier manual-probe failures remain in [prediction evidence](prediction.md).
