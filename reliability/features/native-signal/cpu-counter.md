# Cumulative CPU counter evidence

Run: `artifacts/timer-sampled/20261015T025206017-a2af01e3`.
Target: `LinuxFastTimerCapacity`.
Each percentage ran once. Both result files and all failures remain retained.

The CPU source requests `PERF_SAMPLE_READ` and receives 48-byte sample records.
Samples retain cumulative counts separately from nominal periods.
Four syscall reads bracket the two frozen measurement endpoints.
The source journal records every enable and disable operation.

| Quantity | Zero percent | Fifty percent |
| --- | ---: | ---: |
| Process exit | 0 | 1 |
| Samples | 1,951 | 411 |
| Enabled epochs | 1 | 7 |
| Transition records | 2 | 14 |
| Observed count delta, lower | 1,943,089,300 ns | 409,180,800 ns |
| Observed count delta, upper | 1,943,504,600 ns | 409,726,600 ns |
| Last accepted sample count | 1,942,085,900 ns | 406,005,900 ns |
| Final sample count | 1,953,085,700 ns | 411,006,300 ns |
| Final disabled count | 1,953,328,400 ns | 411,337,600 ns |
| Sum of all nominal periods | 1,951,000,000 ns | 411,000,000 ns |

Both counter journals report zero read failures, no overflow, and no unrecorded cleanup.
All sample counts are nondecreasing and do not exceed the final disabled counter.
Both sets of four readings use one event identity and enclose the frozen endpoints.
Independent recomputation agrees with both saved count ranges and transition sequences.

These are enabled-event counter observations. They do not bound all application CPU or selected-method CPU.
Final disabled counts include shutdown CPU and do not replace the frozen readings.
The zero-percent final count exceeds its nominal period sum by 2,328,400 ns.
This difference is not a missing selected-CPU estimate.

The treatment fails at 2,499,927 ns of peer debt.
It also retains an invalid timer journal, native cancellation status 16, and another invalid journal observation during cleanup.
One native child reports an 8,696,500 ns wait and an invalid record.
Both workers join and retire, but the peer lease remains unreleased and `closed` is false.
Counter consistency does not remove these failures.

The manifest reports unchanged inputs and includes the new counter sources and ring checks.
The overall run fails. Prediction accuracy and sampled capacity remain unverified.

The portable command `--verify-cpu-counters` covers ABI bytes, nine ring groups, and six counter journal/range groups.
The full `--verify-timer-layout` command passes on Windows and Linux x64.
`LinuxPerfVerify` also passes for the direct CPU-event route.
These deterministic checks do not establish runtime accuracy or observer overhead bounds.

## Offline reconciliation

`AnalyzeCpuCounters.py` reconciles both retained captures without another workload execution.
Reports are `counter-analysis-reconciled-0.json` and `counter-analysis-reconciled-50.json` in the same run directory.
Each report retains input and analyzer SHA-256 hashes. Earlier reports remain unchanged.

| Observation | Zero percent | Fifty percent |
| --- | ---: | ---: |
| Count after last accepted sample, lower | 1,003,400 ns | 3,174,900 ns |
| Count after last accepted sample, upper | 1,418,700 ns | 3,720,700 ns |
| Disabled elapsed time, lower | 0 ns | 206,498 ns |
| Disabled elapsed time, upper | 3,014,905 ns | 3,304,688 ns |

These ranges use frozen endpoints. Shutdown samples do not replace the last accepted sample.
Disabled elapsed ranges include endpoint and transition uncertainty.
They do not establish disabled CPU or selected CPU.
The treatment report preserves all four workload failures and `nativeSessionClosed = false`.
Both reports keep all attribution and prediction claim flags false.

Fifteen counter checks and nineteen matched-analyzer checks pass on Windows and Linux with Python.
The Linux checks use `python:3.12-slim`.
The .NET SDK image does not contain Python.
`BuildDocs` passes. No new timed matrix has run for this analyzer increment.

## Nominal credit reconciliation

`sample-credit-cli-0.json` and `sample-credit-cli-50.json` retain the offline replay of the same captures.
Each report includes counter diagnostics, credit diagnostics, input SHA-256, and analyzer SHA-256.
Earlier reports remain unchanged. No workload ran again.
The committed CLI produces these reports with `--sample-credits`:

```powershell
python native/AnalyzeCpuCounters.py artifacts/timer-sampled/20261015T025206017-a2af01e3/sampled-0.json artifacts/timer-sampled/20261015T025206017-a2af01e3/sample-credit-cli-0.json --sample-credits
python native/AnalyzeCpuCounters.py artifacts/timer-sampled/20261015T025206017-a2af01e3/sampled-50.json artifacts/timer-sampled/20261015T025206017-a2af01e3/sample-credit-cli-50.json --sample-credits
```

Existing output paths are rejected. Reanalysis requires a new output filename.

| Observation | Zero percent | Fifty percent |
| --- | ---: | ---: |
| Accepted samples | 1,940 | 405 |
| Qualified samples | 1,915 | 392 |
| Epoch-excluded samples | 1 | 7 |
| Location-excluded samples | 24 | 6 |
| Unaccepted samples | 11 | 6 |
| Qualified nominal periods | 1,915,000,000 ns | 392,000,000 ns |
| Replayed credit | 0 ns | 196,000,000 ns |

Every per-record credit matches the replay. Both totals match their frozen endpoint and final credit fields.
Both fractional remainders are zero.
The treatment's original failures and incomplete native closure remain in the counter diagnostics.
The replay verifies arithmetic under declared epochs. It does not verify native eligibility transitions or prediction accuracy.

See the [counter reference](../../../docsSrc/Reference/cpu-counter-evidence.md),
[attribution limits](../../../docsSrc/Explanations/cpu-counter-attribution.md), and
[Linux perf contracts](../../../lode/profiling/linux-perf.md).
