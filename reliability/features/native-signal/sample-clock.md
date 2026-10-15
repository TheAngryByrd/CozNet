# Sample clock evidence

The CPU event attribute selects `CLOCK_MONOTONIC` explicitly.
The portable evidence parser checks native/managed brackets and namespace evidence.
Neither check establishes a kernel clock error bound or prediction accuracy.

## Deterministic checks

`--verify-cpu-event-clock` checks ABI bytes and nine evidence groups:

- Original readings survive successful parsing.
- Missing evidence and incompatible domains or units reject parsing.
- Mapping failures and changed contexts reject parsing.
- Readings enclose the frozen measurement window.
- Namespace offset text parses without losing nonzero offsets.
- Namespace mismatch becomes a structured rejection without reading unrelated offsets.
- Offset and native-read failures preserve the known context.
- Namespace changes retain the raw reading, while stable capture preserves it without an error.
- Native syscall, value, and conversion failures retain the timestamps captured before failure.

The checks ran on Windows and Linux x64 with exit code zero.
`--verify-timer-layout` includes these checks.

## Retained live runs

Both runs use `LinuxFastTimerCapacity`, two percentages, and the unchanged two-millisecond debt limit.
Each case ran once in each run.

| Run | Zero percent | Fifty percent | Clock evidence |
| --- | --- | --- | --- |
| `20261015T021833864-7f971260` | Exit 0 | Exit 1, peer debt 2,499,841 ns | Rejected: thread proc path has no offset file |
| `20261015T022019094-f917512e` | Exit 0 | Exit 1, peer debt 2,373,189 ns | Both native readings lie inside managed brackets in both cases |

The first run retained the missing `/proc/thread-self/timens_offsets` error.
The corrected adapter reads `/proc/self/timens_offsets` only after checking its namespace identity against the calling thread.
The second run records zero monotonic offsets and stable namespace identities.
Both manifests retain `inputsUnchanged = true` and `passed = false`.
Their source inventories omit the new clock files, although their binary hashes identify the executed assemblies.
The current runner includes those files. The earlier manifest flags do not prove that those sources stayed unchanged.

The second run contains 1,842 samples at zero percent and 41 samples at fifty percent.
Its first sample-to-receipt differences are 328,179 ns and 591,529 ns.
These differences include delivery and controller work. They are not clock-offset estimates or error bounds.
The treatment stops early after its debt failure.

Evidence remains under `artifacts/timer-sampled/<run>/`.
The later metadata change reads the full kernel release from `/proc/sys/kernel/osrelease`.
The retained run used `Environment.OSVersion.VersionString` and records `Unix 5.15.153.1`.
It does not verify the later metadata read.
A separate Linux reflection probe checks the final adapter against `/proc/sys/kernel/osrelease`.
It records `5.15.153.1-microsoft-standard-WSL2`, a zero offset, and a native reading inside the managed bracket.
Its command exits zero. The probe does not rerun the timed workload.
The final adapter also catches `InvalidDataException` and retains structured failures.
Deterministic capture checks cover this failure path on Windows and Linux.
The retained timed runs precede this correction.

## Observed membership replay

The later counter capture `20261015T025206017-a2af01e3` also retains clock evidence.
The versioned observed membership policy accepts both captures without another timed run.
These commands produce `source-membership-v2-0.json` and `source-membership-v2-50.json` in that directory.
Earlier reports remain unchanged.

```powershell
python native/AnalyzeSampleMembership.py artifacts/timer-sampled/20261015T025206017-a2af01e3/sampled-0.json artifacts/timer-sampled/20261015T025206017-a2af01e3/source-membership-v2-0.json
python native/AnalyzeSampleMembership.py artifacts/timer-sampled/20261015T025206017-a2af01e3/sampled-50.json artifacts/timer-sampled/20261015T025206017-a2af01e3/source-membership-v2-50.json
```

Existing output paths are rejected. Reanalysis requires a new output filename.

| Observed membership | Zero percent | Fifty percent |
| --- | ---: | ---: |
| Inside samples | 1,941 | 409 |
| After samples | 10 | 2 |
| Before or ambiguous samples | 0 | 0 |
| Selected inside samples | 1,917 | 402 |
| Selected inside nominal weight | 1,917,000,000 ns | 402,000,000 ns |

The reports preserve raw records and input, analyzer, and counter-parser hashes.
Inside totals include late receipts that the producer did not accept for credit.
They do not replace the producer's credit replay or bound exact selected CPU time.
The treatment retains its debt and native journal failures, cancellation status, and incomplete closure.
Both reports keep physical-clock accuracy, exact event ordering, and prediction accuracy false.
Eight portable checks pass on Windows and Linux with Python.

## Recorded credit membership

The combined mode produces `credit-membership-0.json` and `credit-membership-50.json` from the same retained captures.

```powershell
python native/AnalyzeSampleMembership.py artifacts/timer-sampled/20261015T025206017-a2af01e3/sampled-0.json artifacts/timer-sampled/20261015T025206017-a2af01e3/credit-membership-0.json --sample-credits
python native/AnalyzeSampleMembership.py artifacts/timer-sampled/20261015T025206017-a2af01e3/sampled-50.json artifacts/timer-sampled/20261015T025206017-a2af01e3/credit-membership-50.json --sample-credits
```

| Group | Zero percent | Fifty percent |
| --- | ---: | ---: |
| Recorded credit with inside source time | 0 ns | 196,000,000 ns |
| Recorded credit with ambiguous source time | 0 ns | 0 ns |
| Selected inside samples received after the endpoint | 1 | 3 |
| Their nominal weight | 1,000,000 ns | 3,000,000 ns |
| Unaccepted inside samples received inside the window | 0 | 1 |

The treatment's inside rejection is not a selected sample. It remains separate from the three late selected samples.
All rejected samples retain zero recorded credit. The replay does not add their nominal weights to the ledger.
The treatment's recorded credit lies inside the observed window under the declared clock policy.
This does not verify native eligibility transitions, missing-credit bounds, native payment, or prediction accuracy.
The original failures remain unchanged. No timed workload ran again.
Fourteen membership checks and twenty-eight matched checks pass on Windows and Linux.
The updated timed runner was not executed for this offline increment.

See [observed membership](../../../docsSrc/Reference/sample-membership.md),
[the clock reference](../../../docsSrc/Reference/cpu-sample-clock.md),
[alignment limits](../../../docsSrc/Explanations/sample-clock-alignment.md), and
[Linux perf contracts](../../../lode/profiling/linux-perf.md).
