# Cold sampled timer evidence

The first live comparison is retained in `artifacts/timer-sampled/20261014T220841443-2aa21efd`.
Its manifest contains source hashes, binary hashes, Docker image identities, and timing limits.
The runner confirmed unchanged inputs across both cases.

| Case | Result | Samples | Credit | Released journals | Final peer debt |
| --- | --- | ---: | ---: | ---: | ---: |
| Zero percent | Pass | 1,961 | 0 ns | 0 | 0 ns |
| 50 percent | Capacity failure | 194 | 16,000,000 ns | 1 | 14,999,982 ns |

The treatment failed with `The service lease debt exceeds its capacity bound.` during peer preparation.
Its completed peer lease had a 1,000,000 ns budget and settled 1,000,018 ns of measured wait.
Both cases retired, joined, and released both workers. Both native sessions closed with no pending controller journals.
The portable layout and controller checks also passed in the Linux runner.

The first capture checked peak debt after preparation. The preparation exception therefore bypassed the peak update.
Its reported peer peak was only 1,000,000 ns. The final boundary retained the larger unpaid debt.
The current workload captures a separate failure boundary before shutdown and includes that debt in its peak fields.
The later diagnostic runs below exercise those fields.

This cold comparison does not isolate JIT, controller cost, or scheduling as the cause of the backlog.
It does not establish sampled capacity, general CLR safety, or prediction accuracy.
The incomplete setup directory `artifacts/timer-sampled/20261014T220801517-bd0b1de8` belongs to an earlier manifest-path error.
No workload ran from that directory.

## Controller startup diagnostic

`artifacts/timer-sampled/20261014T221113319-e4405b66` retains the cold phase diagnostic.
Zero percent passes. Treatment fails lease capacity with 13,999,993 ns of peer debt at the failure boundary.
The manifest confirms unchanged inputs.

| Completed phase | Iteration | Controller CPU | Bracketed elapsed interval |
| --- | ---: | ---: | ---: |
| Sample drain | 1 | 22.1359 ms | 22.133640–22.139392 ms |
| Selected preparation | 1 | 2.6754 ms | 2.674234–2.676096 ms |
| Peer preparation | 1 | 3.7438 ms | 3.742363–3.744826 ms |
| Peer poll | 2 | 14.6194 ms | 14.617981–14.621028 ms |

Controller CPU nearly fills these elapsed intervals. This supports investigation of controller startup work.
It does not identify JIT or another specific cause.
The incomplete preparation phase also includes exception formatting in this version. Its duration cannot isolate the failed operation.

## Managed initialization and cleanup

`artifacts/timer-sampled/20261014T221250734-df6ce772` retains the first initialized comparison.
Both cases verify the synthetic 40,000 ns credit, 70,000 ns peer wait, and 30,000 ns selected wait.
Synthetic accounting ends with zero debt and no pending ownership.
Zero percent passes. Treatment fails at 3,999,966 ns of observed peer debt.
Its first drain uses 0.3131 ms of controller CPU.

The treatment also records native cancellation status 16 and a first-receipt failure beyond 100 ms during cleanup.
Both workers join and retire, but peer journal ownership remains unresolved and native close does not occur.
This version formats the primary exception before it collects outstanding receipts.
The record does not separately time that formatting operation.

`artifacts/timer-sampled/20261014T221358492-441463fb` retains the changed cleanup path.
It defers exception formatting and polls pending journals before cancellation.
Both cases again verify the separate synthetic initialization contract. The manifest confirms unchanged inputs.

| Case | Result | Samples | Live credit | Peer peak debt | Cleanup |
| --- | --- | ---: | ---: | ---: | --- |
| Zero percent | Pass | 1,950 | 0 ns | 0 ns | Both workers released, native session closed |
| 50 percent | Debt failure | 12 | 4,000,000 ns | 3,499,914 ns | Both workers released, native session closed |

The first live treatment sample uses epoch 1 and earns zero credit.
The first treatment drain uses 0.2691 ms of controller CPU.
Both peer journals complete with ten children. Neither requires cancellation.
Their submission-to-final-observation intervals are 6.849756 ms and 3.259856 ms.
These intervals include controller and native work. They do not isolate delivery time.

The bounded phase capture fills at iteration 26, before the first peer submission.
It therefore cannot explain those journal intervals or the remaining debt failure.
The next diagnostic must retain phases around actual service work instead of exhausting its budget on empty polls.

## Service phase selection

`artifacts/timer-sampled/20261014T221740627-fb8e527d` retains the revised phase selection.
It keeps submissions and changed journal processing, plus the first drain and drains that consume samples.
Zero percent passes. Treatment fails at 4,099,577 ns of observed peer debt.
Both workers release and the native session closes with no pending ownership.
The manifest confirms unchanged inputs. Ten portable controller checks pass in the Linux runner.

The treatment retains 11 phase records, including both peer submissions and their receipt processing.
The first peer submission uses 1.0749 ms of controller CPU within 1.075555 ms elapsed.
The first peer journal read uses 6.8082 ms CPU within 6.811162 ms elapsed.
The next changed peer journal read uses 0.4013 ms CPU within 0.402025 ms elapsed.
Journal phases include native reading, receipt parsing, ledger application, and any release.
These records locate substantial controller execution inside the first real receipt path.
They do not identify a particular method or JIT event.

The dedicated native setup experiment exercises that path before live measurement without using live registrations or ledger credit.

## Native setup session constraint

`artifacts/timer-sampled/20261014T222019538-6dc3ad74` retains the first native setup attempt.
Both setup sessions complete their deferred commands and close successfully.
Both subsequent live session opens fail with native status 114, `EALREADY`.
No live samples or timer measurements occur.
The native API intentionally permits only one session per process.

The corrected setup uses slot 2 in the same session and releases that target before live workers register in slots 0 and 1.
This preserves the one-session contract and keeps setup separate from live accounting and slot generations.

## Dedicated native setup slot

`artifacts/timer-sampled/20261014T222124331-7f2d3a84` retains the corrected setup-slot comparison.
Both native setup commands defer with zero wait and release their dedicated target.
Zero percent passes with 1,932 samples, zero live credit, and no live timer commands.
Treatment fails at 2,097,751 ns of observed peer debt after six released journals.
Both cases close the native session with no pending ownership. The manifest confirms unchanged inputs.

The first live peer submission uses 0.1322 ms of controller CPU within 0.132945 ms elapsed.
The first live journal-processing phase uses 0.6752 ms CPU within 0.677169 ms elapsed.
The first treatment sample remains a fresh epoch baseline and earns no credit.
These records show lower initial service costs than the managed-only diagnostic.
They do not establish stable capacity or isolate all causes of the remaining debt failure.

The next variant removes per-iteration boundary allocation and uses typed native-struct equality.
It preserves per-iteration debt checks and all timing limits.

## Scalar debt checks and typed equality

`artifacts/timer-sampled/20261014T222245220-8017d753` retains this variant.
Zero percent passes with 1,918 samples, no live credit, and no live commands.
Treatment completes 13 peer journals, then fails lease capacity at 2,043,571 ns of unpaid peer debt.
It records 35 samples and 15,000,000 ns of live credit.
The completed waits total 12,956,429 ns, which reconciles with the retained debt.
Both native setup slots release. Both live sessions close with no pending controller ownership.
The manifest confirms unchanged inputs, and the Linux layout and portable controller checks pass.

The first peer submission uses 0.0862 ms controller CPU within 0.086801 ms elapsed.
An early peer journal read uses 0.5906 ms CPU within 0.591315 ms elapsed.
The phase budget still fills before the failing interval, so these records do not establish its cause.
No per-iteration allocation measurement accompanies this run. Source changes alone do not quantify total allocation reduction.
Capacity and prediction accuracy remain unverified.

## Rolling phase capture and probe timestamps

`artifacts/timer-sampled/20261014T222516812-7c3162bb` retains the rolling phase diagnostic.
Zero percent passes. Treatment fails at 2,299,292 ns of peer debt and completes cleanup.
The failure boundary records 6,500,000 ns global delay, 3,800,373 ns settled peer wait, and 400,335 ns active peer wait.
These values reconcile with the reported debt.
The output keeps the final 128 phase records and reports 787 overwritten records.

The retained interval contains repeated `read-peer` operations on adjacent controller iterations.
Source inspection found that a zero-capacity probe returned `ENOBUFS` before setting `captured_ns` once any child existed.
The controller rejected that zero timestamp as older than its last full capture.
It therefore read and parsed the journal again even when all other header fields were unchanged.

The native snapshot now timestamps an insufficient-buffer header after copying the core snapshot.
It still copies no child data and does not authorize release.
`artifacts/timer-api/20261014T222647486-5a3d6709` passes the native contract, deferred, and chained-wait checks.
Those checks require a nonzero probe timestamp, a nondecreasing full-copy timestamp, and rejection of release after an incomplete copy.

`artifacts/timer-sampled/20261014T222712965-a017238e` retains the sampled comparison with corrected probe timestamps.
Zero percent passes with 2,008 samples and no live commands.
Treatment fails at 2,299,635 ns peer debt after ten journals. Cleanup completes with no pending ownership.
The manifest confirms unchanged inputs. Portable layout, controller, and phase-retention checks pass in Linux.
The rolling capture retains 128 records and replaces 85 older records.

The longest retained sample drain takes 0.639009 ms elapsed and 0.0664 ms controller CPU.
The longest retained journal-processing phase takes 0.035938 ms elapsed and 0.0349 ms CPU.
This capture no longer shows the earlier repeated full-read pattern or a multi-millisecond receipt-processing phase.
It does not measure total allocation or identify the scheduler decision behind the drain interval.

Generations 9 and 10 defer after partial payment. Their last children perform no wait outside the selected range.
Their first arm-to-handler intervals are 1.005450 ms and 1.009029 ms.
The configured initial timer interval is 1 ms, while continuation intervals are 10 µs.
These observations make the per-lease initial interval and deferral behavior relevant to the next experiment.
They do not prove that shortening the initial interval will meet capacity or preserve all runtime-transition behavior.

## Initial interval configuration

`artifacts/timer-api/20261014T223126817-bb38d594` passes the native configuration and existing timer checks.
It accepts 10,000 and 1,000,000 ns before registration, rejects unsupported values, and rejects interval changes after registration.
The default remains 1 ms. Continuation intervals and wait, receipt, handler, and debt bounds remain unchanged.
The managed interval parser accepts only `1ms` and `10us`.

The phase checks also retain their original failed-interval, prefix-limit, and regressed-clock cases alongside the three rolling-buffer cases.
All six phase checks run through the portable timer verifier.

`artifacts/timer-sampled/20261014T223217306-d1138ef0` retains a failed launcher attempt.
PowerShell split the single string `10us` into four arguments. Both processes rejected their arguments before measurement.
The corrected runner builds an explicit argument array and retains that array in each outcome.

`artifacts/timer-sampled/20261014T223307262-8066e913` retains the corrected 10 µs diagnostic.
Zero percent passes with 1,953 samples and no live commands.
Treatment completes 2,072 journals and records 1,833 samples before failing at 2,449,816 ns of peer debt.
Both cases close with no pending ownership. The manifest confirms unchanged inputs and a 10,000 ns initial interval.
The treatment diagnostic replaces 23,658 older phase records.
This is substantially more completed service than the earlier 1 ms cases, but it is not a capacity pass.

The next variant disables phase capture while preserving the same preparation, interval, accounting, and bounds.
This separates capacity measurement from continuous diagnostic clock reads and phase allocation.

## First capacity pass without phase capture

`artifacts/timer-sampled/20261014T223424142-a26b03fc` retains the first passing two-case capacity run.
Both cases use 10 µs initial delivery, managed initialization, and dedicated native setup.
The phase fields are null in both records. The manifest confirms unchanged inputs and exact launch arguments.

| Case | Samples | Released journals | Selected journals | Peak selected debt | Peak peer debt |
| --- | ---: | ---: | ---: | ---: | ---: |
| Zero percent | 1,915 | 0 | 0 | 0 ns | 0 ns |
| 50 percent | 1,981 | 2,360 | 48 | 304,733 ns | 999,998 ns |

Both cases complete their two-second measurement windows, retain stable method ranges, and close native sessions without pending ownership.
The treatment records 923,500,000 ns of selected credit.
Its final boundary retains 73 ns of selected debt and zero peer debt. Shutdown does not forgive that remainder.
The portable verifier also passes, including all six phase checks and interval parser checks.

This run proves the bounded capacity checks for these two executions.
It does not establish repeatability, forced-GC capacity, runtime-transition safety with 10 µs initial delivery, or physical prediction accuracy.
All earlier timing failures remain retained. No timing or accounting bound changed to obtain this result.

## Sampled forced-GC reuse failure

`artifacts/timer-sampled/20261014T223833755-2c72533f` retains the first sampled forced-GC comparison.
Zero percent passes with 1,808 samples and 91 collection calls. Its largest collection call is 3.328100 ms.
Treatment completes 1,463 journals before submission of command 1,464 returns native status 16, `EBUSY`, with generation zero.
The previous peer command released generation 1,444.
Treatment peak debt stays below 2 ms: 336,467 ns selected and 1,049,238 ns peer.
The failure boundary retains 299,816 ns peer debt.

Treatment records 73 collection calls, with a maximum duration of 3.682779 ms.
Both GC workers join and establish measurement reach.
Both target threads retire and join, but the ambiguous submitted lease retains peer controller ownership and prevents native close.
The run fails. Its successful GC reach does not turn the protocol failure into a capacity pass.

Source inspection found a publication gap: `TargetLease::retire` exposes core retirement before `LeaseMailbox::quiesce` permits reuse.
A full core snapshot could authorize release during that gap, followed by `EBUSY` from the next mailbox submission.
The record does not directly capture mailbox phase, so it does not independently prove which native `EBUSY` branch executed.

The API now uses `captureTimerJournal` to require matching reader generation and mailbox quiescence before exposing retired visibility.
Core retirement remains visible as quiesced while the mailbox still owns execution.
`coreRetirementCannotAuthorizeMailboxReuse` holds that intermediate state and proves reuse stays blocked until quiescence and reader release.
`readerMustMatchJournalGeneration` rejects a snapshot from another generation.
`artifacts/timer-api/20261014T224141854-52581ff0` passes these portable checks and the native deferred and chained-wait cases.

## Sampled forced GC after the retirement fix

`artifacts/timer-sampled/20261014T224216259-3f268a29` retains the comparison after the mailbox-retirement fix.
The manifest confirms unchanged inputs. Zero percent passes. Treatment fails the unchanged 2 ms debt limit.

| Case | Samples | Released journals | Collection calls | Native session closed |
| --- | ---: | ---: | ---: | --- |
| Zero percent | 1,825 | 0 | 92 | Yes |
| 50 percent | 134 | 143 | 6 | Yes |

Treatment fails after approximately 143.51 ms with 2,499,975 ns of peer debt.
Both GC workers join and meet the measurement-window reach condition. Both native sessions close with no pending ownership.
This run reports a debt failure instead of the previous submission failure.
Its shorter measurement window does not prove that the rare submission failure cannot recur.
The pending-command analysis below narrows the failure interval.
No timing bound changed. The earlier failures remain retained.

### Pending command at the first debt violation

`pending-command-analysis.json` in the same evidence directory records values derived from `sampled-50.json`.
The first excessive boundary precedes the exception boundary. Times below are relative to measurement start.

| Observation | Value |
| --- | ---: |
| Peer command / generation | 143 / 142 |
| Requested lease budget | 499,975 ns |
| Managed submission timestamp | 137.358528 ms |
| First excessive boundary | 141.358552 ms |
| Peer debt at that boundary | 2,499,975 ns |
| Exception boundary | 143.517437 ms |
| Final journal observation during shutdown | 147.774004 ms |
| First child arm-to-handler interval | 9,791,273 ns |
| First child handler interval | 53,101 ns |
| First child confirmed wait | 50,028 ns |

Four accepted samples arrive at 138.355283, 139.352105, 140.354261, and 141.357168 ms.
Each adds 500,000 ns of credit. Their perf timestamps also advance by approximately 1 ms.
These receipts do not show a clustered sample backlog at the first violation.
The controller records rising debt while the peer command remains pending with zero active confirmed wait.
This excludes a complete controller stall across that four-millisecond interval. It does not measure continuous controller execution.

No recorded forced collection overlaps the managed submission-to-observation interval.
The last recorded collection ends at 116.653420 ms, before command submission.
This excludes direct overlap with the recorded forced collection calls. It does not exclude other runtime suspensions or indirect GC effects.

Native child timestamps establish the arm-to-handler interval without a managed/native clock alignment assumption.
Managed timestamps establish sample receipt, boundary, command, and forced-collection ordering using the recorded 1 GHz timestamp frequency.
The analysis does not subtract perf sample timestamps from managed timestamps.

The native handler reads its start clock before it searches the target registrations.
The long interval therefore precedes that first clock reading, rather than occurring in the measured wait.
Target descheduling, timer expiration and signal delivery, and execution before the first clock reading remain possible causes.
The next diagnostic needs target scheduler records around native timer intervals.
It must preserve the original debt gate, retain failed runs, and report capture loss and observer cost.

See [the workload contract](../../../docsSrc/Reference/sampled-timer.md) and [the run procedure](../../../docsSrc/How_Tos/run-sampled-timer.md).

## First scheduler capture

`artifacts/timer-sampled/20261014T225137526-42163a57` retains the first `LinuxFastTimerScheduler` comparison.
The portable layout outcome and zero-percent case pass. Treatment fails at 2,500,000 ns peer debt.
The manifest confirms unchanged inputs. Both sessions close, and all scheduler collectors stop.
All six capture streams report zero final loss and no capture failures.

Treatment command 1 uses peer generation 1. Its first child records a 2,353,895 ns arm-to-handler interval.
`scheduler-overlap-analysis.json` retains the source hash, interval endpoints, and overlapping switch pairs.
All three capture windows contain the complete native interval. Their thread identities are distinct within one process.
The analysis checks record ordering, switch pairing, capture bounds, and loss before computing overlap.

| Stream | Switch-out overlap with arm-to-handler interval |
| --- | ---: |
| Controller | 0 ns |
| Selected target | 0 ns |
| Peer target | 2,349,871 ns |

The peer switch-out carries the preemption flag. Its switch-in precedes the first handler timestamp by 4,024 ns.
This establishes peer descheduling during almost the entire pending delivery interval in this run.
It does not establish the reason for preemption or distinguish diagnostic overhead from ordinary scheduling pressure.

The first excessive boundary occurs 6.791454 ms after measurement start.
The exception boundary occurs at 8.637528 ms. The final journal arrives during shutdown.
The first forced collection finishes at 1.426809 ms, before command submission at 4.707407 ms.
The initial sample drain also contains queued samples. This startup failure differs from the earlier late failure.
Do not use this capture to assign the earlier failure's cause.

Scheduler capture adds three collector threads and their buffers. Observer cost remains unmeasured.
Portable scheduler decoding checks already exist. Dedicated timer scheduler shutdown fault checks remain to be added.
The next implementation decision must account for peer descheduling without forgiving delay debt or changing the 2 ms gate.

### Portable scheduler shutdown checks

`TimerSchedulerCaptureGroup` supplies the registration and retirement gate used by `SampledTimerWorkload`.
Seven portable checks exercise that helper through the existing `--verify-timer-layout` entry point.

| Check | Required behavior |
| --- | --- |
| `DisabledCaptureAllowsRetirement` | Disabled diagnostics do not block target retirement. |
| `FinishPrecedesRetirement` | Every collector stops before retirement. Repeated completion preserves evidence. |
| `FailedDisposalRetainsTarget` | The actual worker lifetime retains ownership after failed collector disposal. |
| `FinishFailurePreservesEvidenceAndDisposes` | Capture failure remains recorded after successful disposal. |
| `LossRemainsFailureAfterCleanup` | Missing loss counts, loss counters, loss records, and capture errors remain failures. |
| `RegistrationCannotReplaceOrReopen` | Duplicate and late registration cannot create or replace a collector. |
| `PartialRegistrationStillStopsExistingCollectors` | Partial startup still disposes existing collectors. |

These checks use the production helper and injected scheduler sessions. They do not simulate kernel perf failures.
The final seven-check suite passes on Windows and in `mcr.microsoft.com/dotnet/sdk:10.0.100` on Linux.
Both executions use the Release workload and `--verify-timer-layout`.
`FinishPrecedesRetirement` also attempts retirement inside every disposal callback, including the last collector.
All three attempts remain blocked until completion. `BuildDocs` passes after the helper and documentation updates.
Logs remain under `artifacts/timer-sampled/scheduler-shutdown-*`.

## Scheduler capture throughput comparison

`artifacts/timer-sampled/20261014T225710258-7fed9662` retains the `LinuxTimerSchedulerCost` run.
All 16 zero-percent cases and the portable layout outcome pass. The manifest confirms unchanged source and binary inputs.
Each of eight pairs contains capture enabled and disabled with forced GC. Seed 20261014 selects the order within each pair.
Every case completes its two-second window, establishes GC reach, and closes without pending ownership.
Every enabled case has three distinct scheduler thread identities, zero reported loss, and valid ordered switch records.

The retained `analyze-cost.py` validates these conditions before computing throughput from `measurementProgress` and its timestamp brackets.
`cost-analysis.json` retains each input hash, counter snapshot, rate bounds, and paired change.
The copied `AnalyzeScheduler.py` supplies the scheduler-integrity checks.
The final counters are not used because they include shutdown work.

| Pair | Enabled throughput change relative to disabled |
| --- | ---: |
| 1 | +13.42% |
| 2 | +2.18% |
| 3 | +1.80% |
| 4 | +34.88% |
| 5 | −6.51% |
| 6 | +6.56% |
| 7 | −16.43% |
| 8 | +7.31% |

The mean paired change is +5.40%. A descriptive Student-t 95% interval spans −7.16% to +17.96%.
Eight noisy pairs do not establish a speedup or a small overhead bound.
The interval summarizes this run. It does not establish independence across host scheduling conditions or guarantee future performance.
This comparison includes sampling and forced GC but no live timer commands.
It therefore does not measure treatment overhead, process CPU cost, or prediction accuracy.
All outcomes remain retained. No failed case was replaced or timing limit changed.

## Controlled unavailable-target accounting

`UnavailableTargetRetainsGrowingDebt` drives the real controller with selected observations and an active empty timer journal.
The submitted lease stays at 110,000 ns while new sample credit raises debt to 1,999,999, 2,000,000, and 2,000,001 ns.
The shared live-workload gate accepts the first two values and rejects the third for either slot.
No receipt means no confirmed wait, even as native capture timestamps advance.
Cancellation releases the retired empty journal with zero settled wait and no pending reservation.
The ledger retains 2,000,001 ns of debt. It rejects an oversized replacement lease.

`DelayedDeliveryCreditsOnlyConfirmedWait` presents an empty journal, followed by a child whose first handler timestamp is 9,000,000 ns.
Its arm timestamp is 999 ns. The controller credits only the child's 40,000 ns measured wait.
Cancellation settles that wait once and retains 70,000 ns of debt.
The delivery gap does not become wait credit.

These deterministic checks model unavailable target service through `ITimerTransport`.
They do not cause OS preemption or validate Linux timer delivery under a controlled scheduling policy.
Both checks pass through the Release `--verify-timer-layout` entry point on Windows and Linux.
At that time, the complete layout suite included twelve controller checks. `BuildDocs` also passed.
Build and Linux execution logs remain under `artifacts/timer-sampled/unavailable-target-*`.

## Diagnostic failure and cleanup regression

The controller retains operation errors together with diagnostic closing-clock errors.
Successful native release records cumulative wait before phase closure. A later diagnostic failure updates the archived journal without repeating release.
The workload reads those totals for debt checks and output.

`CombinedOperationAndPhaseFailuresRemainVisible` covers submission, read, and accounting errors with closing-clock errors.
`ClosingClockFailurePreservesReleasedWait` checks retained payment, archived failure, zero debt, and no repeated release.
`CancellationCanObserveCompletedOrDeferredJournal` covers terminal journals that race successful cancellation intent.
Three `TimerSessionCleanup` checks cover successful ordering, sample-disposal failure, and multiple cleanup failures.

The initial regression fails against the previous controller in `artifacts/timer-sampled/phase-regression-red.log`.
That log records the combined-failure check but does not retain source hashes. It does not prove failure of every new check against earlier code.
The Linux Release verifier passes through `--verify-timer-layout`.
The `layout` outcome records exit code zero in `artifacts/timer-sampled/20261014T232516660-9f6304ad/summary.json`.
That run retains source and binary hashes. `phase-regression-green-build.log` records the successful Release build only.
The empty `phase-regression-linux.log` does not independently prove execution. This evidence set does not retain a Windows verifier exit record.
These checks use injected failures. They do not establish sampled capacity or prediction accuracy.

## Live regression after diagnostic fixes

`artifacts/timer-sampled/20261014T232516660-9f6304ad` retains the prepared `LinuxFastTimer` comparison with phase diagnostics and 10 µs initial delivery.
This run does not force GC. Zero percent passes. Treatment fails the unchanged 2,000,000 ns debt limit.
Treatment peaks at 5,499,905 ns of peer debt and releases 672 journals.
Its serialized released waits are 1,013 ns selected and 286,501,134 ns peer.
Both sessions close without pending controller ownership.

The failure remains retained. Successful cleanup does not establish capacity or prediction accuracy.

## Cumulative released-wait regression

`ReleasedWaitAccumulatesAcrossGenerations` releases 40,000 ns from a cancelled generation and 70,000 ns from the next completed generation in the same slot.
It requires 110,000 ns cumulative wait, no remaining debt or ownership, two native releases, and zero wait in the other slot.
An idle poll must preserve the total and release count.
The Release `--verify-timer-layout` verifier passes on Windows and Linux with this check.
The Linux execution uses `mcr.microsoft.com/dotnet/sdk:10.0.100` with the Release directory mounted read-only.
The console verifier emits no case counts. Source inspection identifies sixteen controller checks.

The isolated source snapshot starts from tree `0cdcf3b6bdbf556e1f85d6f4f3b28d79cb5d73ee` and includes the revised checks file.
Its baseline builds and passes the same verifier.
The manual mutant replaces `checked(releasedWaits[slot] + adapter.ConfirmedNanoseconds)` with `adapter.ConfirmedNanoseconds`.
The mutant builds, then fails in `ReleasedWaitAccumulatesAcrossGenerations` with the expected cumulative-wait assertion.
Verdict: **Detected**. This proves sensitivity to overwriting the accumulated wait. It does not establish sampled capacity or accuracy.
The mutation never changes the repository controller. The isolated source is restored after the run.

Evidence remains under `artifacts/timer-sampled/`:

| Artifact | Evidence |
| --- | --- |
| `cumulative-wait-build.log` | Release build and exit code. |
| `cumulative-wait-windows.log`, `cumulative-wait-linux.log` | Verifier exit code zero on each platform. |
| `cumulative-wait-snapshot-build.log`, `cumulative-wait-snapshot-baseline.log` | Isolated baseline build and verifier exit codes. |
| `cumulative-wait-mutation.json` | Exact expression change, controller fingerprints, checks fingerprint, and base tree. |
| `cumulative-wait-mutant-build.log`, `cumulative-wait-mutant.log` | Successful mutant build, expected assertion failure, stack, and nonzero exit code. |

Both snapshot builds use `dotnet build tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj -c Release`.
The mutant build also uses `--no-restore`.
Both executions use `dotnet tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll --verify-timer-layout`.
