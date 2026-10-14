# Timer worker lifecycle evidence

The lifecycle change starts from commit `76c5f631859ee45c25ededb803c9b56ebcdd91d5`.
`TimerSelectedWorker` uses `TimerWorkerLifetime` for the same operations exercised by the portable checks.
The component preserves primary and retirement failures together.
It requires confirmed retirement and join before disposing readiness or releasing a journal.

## Portable checks

`TimerLayoutVerify` passes on Windows x64.
The Linux matrix also runs the layout checks before its eight live cases.
The new checks use controlled callbacks without native calls, real delays, or background threads.

| Check | Required observation |
| --- | --- |
| `SuccessfulCleanupOrder` | Operations occur in order, and repeated completed cleanup performs no effects |
| `RegistrationFailureStillRetires` | Partial registration still retires, and its original error survives successful cleanup |
| `RegistrationAndRetirementFailuresRetainBoth` | Both failures remain available, and failed join prohibits disposal and release |
| `BodyFailureStillRetires` | Body failure still retires and remains a failure after both initial and repeated cleanup |
| `RetirementFailurePreventsReleaseDespiteJoin` | A join result cannot replace retirement confirmation |
| `FailedJoinPreservesResources` | Failed join retains readiness and journal ownership |
| `JoinExceptionPreservesResources` | Join exceptions propagate without disposal or release |
| `FailedReleasePreventsClose` | Failed release leaves the close prerequisite false |
| `FailedDisposalPreventsRelease` | Disposal failure stops cleanup before release |

## Mutation checks

The final isolated evidence is `artifacts/timer-lifetime-mutations/20261014T215013464`.
The baseline copy passes all nine checks with .NET SDK 10.0.112 on Windows x64.
Each mutation uses a separate project containing exact copies of the lifecycle component and checks.
`summary.json` retains the source hashes, mutation replacements, command, exit codes, and verdicts.
Each case retains its source, project, and raw `run.log`.
The working source and checks remain unchanged.

| Mutation | Detecting check | Verdict |
| --- | --- | --- |
| Remove the retirement prerequisite | `RetirementFailurePreventsReleaseDespiteJoin` | Detected |
| Remove the join prerequisite | `FailedJoinPreservesResources` | Detected |
| Set `Released` before release returns | `FailedReleasePreventsClose` | Detected |
| Replace the primary error with the retirement error | `RegistrationAndRetirementFailuresRetainBoth` | Detected |
| Repeat effects after completed cleanup | `SuccessfulCleanupOrder` | Detected |

All five mutants compile and fail at the expected check.
These detections establish sensitivity to the five listed defects, not complete lifecycle correctness.
The earlier four-mutant run remains at `artifacts/timer-lifetime-mutations/20261014T214340754`.

## Live integration

The final retained matrix is `artifacts/timer-transitions/20261014T215056461-0434bd7e`.
All eight scenarios pass with unchanged source and binary hashes.
Every case reports `selectedRetired`, `selectedJoined`, and `selectedReleased` as `true`.
Every final accounting state has zero pending reservations.
The transition cases record 37 selected payments without GC and 59 with GC.
The earlier lifecycle matrix remains at `artifacts/timer-transitions/20261014T214213634-5dd8fa6d`.

The portable checks prove managed decisions for supplied outcomes.
The live matrix proves ordinary cleanup with the real backend in these cases.
Neither establishes kernel fault outcomes, exhaustive thread interleavings, general CLR safety, sampled capacity, or physical prediction accuracy.

See [the lifecycle reference](../../../docsSrc/Reference/timer-worker-lifetime.md) and [the transition procedure](../../../docsSrc/How_Tos/run-timer-transitions.md).
